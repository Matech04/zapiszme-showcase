using App.Application.Booking;
using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Notifications;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.SelfService.Commands;

public record RequestSelfServiceOtpCommand(string Slug, string? Email, string? PhoneNumber) : IRequest;

internal sealed class RequestSelfServiceOtpCommandHandler : IRequestHandler<RequestSelfServiceOtpCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IBookingOtpEmailSender _emailSender;
  private readonly IPhoneOtpSender _phoneOtpSender;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly INotificationUsageRecorder _usageRecorder;
  private readonly ISmsUsageGuard _usageGuard;
  private readonly IUnitOfWork _uow;
  private readonly TimeProvider _timeProvider;
  // Opcjonalny: DI wstrzyknie w prod (AddHttpContextAccessor). Param na końcu z default null,
  // by testy jednostkowe tworzące handler wprost nie wymagały argumentu (clientIp → null = no-op).
  private readonly IHttpContextAccessor? _httpContextAccessor;

  private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan OtpRequestCooldown = TimeSpan.FromSeconds(60);

  public RequestSelfServiceOtpCommandHandler(
    IApplicationDbContext context,
    IBookingOtpEmailSender emailSender,
    IPhoneOtpSender phoneOtpSender,
    IBookingOtpProtection otpProtection,
    INotificationUsageRecorder usageRecorder,
    ISmsUsageGuard usageGuard,
    IUnitOfWork uow,
    TimeProvider timeProvider,
    IHttpContextAccessor? httpContextAccessor = null)
  {
    _context = context;
    _emailSender = emailSender;
    _phoneOtpSender = phoneOtpSender;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _usageRecorder = usageRecorder;
    _usageGuard = usageGuard;
    _uow = uow;
    _timeProvider = timeProvider;
  }

  public async Task Handle(RequestSelfServiceOtpCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Slug == request.Slug, ct)
      ?? throw new NotFoundException(nameof(Tenant), request.Slug);

    var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
    var hasPhone = !string.IsNullOrWhiteSpace(request.PhoneNumber);
    if (hasEmail == hasPhone)
    {
      throw new AppointmentBookingRuleException(
        "Podaj dokładnie jeden kontakt: e-mail albo numer telefonu.",
        ErrorCodes.AppointmentOtpMissingContact);
    }

    string? normalizedEmail = null;
    string? normalizedPhone = null;
    if (hasEmail)
    {
      normalizedEmail = request.Email!.Trim().ToLowerInvariant();
    }
    else
    {
      var phone = new PhoneNumber(request.PhoneNumber!);
      // Anti toll-fraud: SMS tylko na numery polskie (+48) — jak w RequestOtpCommand. Numer
      // zagraniczny/premium w self-service jest nielegitny dla ICP i służyłby drenażowi kredytów.
      if (!phone.IsPolish)
      {
        throw new AppointmentBookingRuleException(
          "Obsługujemy wyłącznie polskie numery telefonu (+48).",
          ErrorCodes.AppointmentOtpUnsupportedPhoneRegion);
      }
      normalizedPhone = phone.Value;
    }

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    var recent = await _context.SelfServiceOtps
      .Where(o => o.TenantId == tenant.Id
        && (normalizedEmail != null
              ? o.Email == normalizedEmail
              : o.PhoneE164 == normalizedPhone))
      .OrderByDescending(o => o.CreatedAtUtc)
      .FirstOrDefaultAsync(ct);

    if (recent is not null && nowUtc < recent.CreatedAtUtc.Add(OtpRequestCooldown))
    {
      throw new AppointmentBookingRuleException(
        "Poczekaj chwilę przed wysłaniem kolejnego kodu.",
        ErrorCodes.RateLimitExceeded);
    }

    // CSPRNG zamiast Random.Shared (przewidywalny) — kod bramkuje mennicę zweryfikowanej sesji.
    // Pełny zakres 000000–999999 (stary Next(100000, 999999) gubił zera wiodące i 999999).
    var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    var hash = SelfServiceCodeHasher.Hash(code);

    var otp = hasEmail
      ? SelfServiceOtp.ForEmail(tenant.Id, normalizedEmail!, hash, nowUtc, OtpTtl)
      : SelfServiceOtp.ForPhone(tenant.Id, normalizedPhone!, hash, nowUtc, OtpTtl);

    await _context.SelfServiceOtps.AddAsync(otp, ct);

    if (hasEmail)
    {
      // Anti email-bomb: globalny cap per-target-email niezależny od tenantów/IP.
      _otpProtection.AssertCanSendOtpToEmail(normalizedEmail!);
      await _emailSender.SendOtpCodeAsync(
        normalizedEmail!,
        code,
        EmailBrand.ForTenant(tenant.Name, tenant.BookingCalendarColorHex),
        ct);
      _otpProtection.RegisterOtpSentToEmail(normalizedEmail!);
    }
    else
    {
      // Anti SMS-bomb: cap SMS-ów na docelowy numer NIEZALEŻNIE od tenanta/IP — TEN SAM
      // licznik IBookingOtpProtection co publiczna rezerwacja (RequestOtpCommand).
      _otpProtection.AssertCanSendOtpToPhone(normalizedPhone!);
      // [M1] Anti-rotacja-numeru: cap WYSŁANYCH OTP-SMS z tego IP/godzinę, niezależny od numeru —
      // ten sam licznik co publiczna rezerwacja (RequestOtpCommand). Bez tego rotacja numeru z 1 IP
      // omijała per-numer capy.
      var clientIp = BookingClientIp.From(_httpContextAccessor?.HttpContext);
      _otpProtection.AssertCanSendOtpSmsFromIp(clientIp);
      // Anti-abuse: twardy miesięczny limit SMS salonu. Po jego osiągnięciu blokujemy nawet OTP
      // (ochrona kredytów ważniejsza niż dokończenie self-service online).
      if (!await _usageGuard.IsWithinMonthlyCapAsync(tenant.Id, ct))
      {
        throw new SmsServiceUnavailableException(
          "Miesięczny limit SMS został wyczerpany — spróbuj później lub skontaktuj się z salonem.");
      }
      await _phoneOtpSender.SendOtpAsync(normalizedPhone!, code, ct);
      _otpProtection.RegisterOtpSentToPhone(normalizedPhone!);
      _otpProtection.RegisterOtpSmsSentFromIp(clientIp);
      // OTP self-service to realna wysyłka (SMS = koszt) — liczymy ją do zużycia salonu.
      await _usageRecorder.RecordSmsAsync(
        tenant.Id, NotificationType.CustomerVerificationOtp, success: true, points: 0m, ct);
    }

    await _uow.SaveChangesAsync(ct);
  }
}
