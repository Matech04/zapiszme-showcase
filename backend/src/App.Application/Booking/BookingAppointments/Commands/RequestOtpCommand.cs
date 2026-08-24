using App.Application.Booking;
using App.Application.Common;
using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Application.Booking.BookingAppointments.Commands;

public record RequestOtpCommand(
  Guid Token,
  Guid AppointmentId,
  string? PhoneNumber,
  string? Email,
  string? FirstName = null,
  string? LastName = null,
  bool IsReturningCustomer = false) : IRequest;

internal sealed class RequestOtpCommandHandler : IRequestHandler<RequestOtpCommand>
{
  private readonly IAppointmentRepository _appointmentRepository;
  private readonly IApplicationDbContext _context;
  private readonly IBookingOtpEmailSender _emailSender;
  private readonly IPhoneOtpSender _phoneOtpSender;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly INotificationUsageRecorder _usageRecorder;
  private readonly ISmsUsageGuard _usageGuard;
  private readonly IUnitOfWork _uow;
  private readonly BookingHoldOptions _holdOptions;
  private readonly TimeProvider _timeProvider;
  private readonly ICurrentTenantService _currentTenant;

  public RequestOtpCommandHandler(
    IAppointmentRepository appointmentRepository,
    IApplicationDbContext context,
    IBookingOtpEmailSender emailSender,
    IPhoneOtpSender phoneOtpSender,
    IBookingOtpProtection otpProtection,
    IHttpContextAccessor httpContextAccessor,
    INotificationUsageRecorder usageRecorder,
    ISmsUsageGuard usageGuard,
    IUnitOfWork uow,
    IOptions<BookingHoldOptions> holdOptions,
    TimeProvider timeProvider,
    ICurrentTenantService currentTenant)
  {
    _appointmentRepository = appointmentRepository;
    _context = context;
    _emailSender = emailSender;
    _phoneOtpSender = phoneOtpSender;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _usageRecorder = usageRecorder;
    _usageGuard = usageGuard;
    _uow = uow;
    _holdOptions = holdOptions.Value;
    _timeProvider = timeProvider;
    _currentTenant = currentTenant;
  }

  public async Task Handle(RequestOtpCommand request, CancellationToken cancellationToken)
  {
    var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Defense-in-depth: wizyta MUSI należeć do tenanta ze slugu (zob. WARNING w AppointmentRepository).
    if (_currentTenant.TenantId is { } currentTenantId && appointment.TenantId != currentTenantId)
    {
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    if (appointment.Lease is null || !appointment.Lease.IsValid(request.Token))
    {
      throw new ForbiddenAccessException(
          "Nieprawidłowy lub wygasły token rezerwacji.",
          ErrorCodes.AppointmentOtpInvalidLease);
    }

    var clientIp = BookingClientIp.From(_httpContextAccessor.HttpContext);
    _otpProtection.AssertCanRequestOtp(request.AppointmentId, clientIp);

    var tenant = await _context.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == appointment.TenantId, cancellationToken)
        ?? throw new NotFoundException("Tenant", appointment.TenantId);

    // [M4] Gate subskrypcji w write-flow: nieaktywny salon (PastDue / Canceled / wygasły Trial) nie
    // może wysyłać OTP (drenaż SMS / fałszywa rezerwacja). Read-query GetPublicBookingSalon zwracał
    // IsBookingAvailable=false, ale spreparowany klient mógł wołać request-otp z pominięciem read.
    var effectiveStatus = tenant.Subscription.EffectiveStatus;
    if (effectiveStatus != SubscriptionStatus.Trial && effectiveStatus != SubscriptionStatus.Active)
    {
      throw new BookingUnavailableException();
    }

    // Imię i nazwisko wymagane przez salon — walidujemy PRZED wysłaniem OTP, żeby nie marnować
    // SMS/maila (koszt) na rezerwację, która i tak zostanie odrzucona na kroku weryfikacji.
    // Wyjątek „umawiam ponownie": stały klient (rozpoznawany po kontakcie dopiero PO weryfikacji OTP)
    // może pominąć imię — wczesny check luzujemy, realna kontrola jest w verify-otp (anti-enumeracja).
    if (tenant.RequireCustomerName && !request.IsReturningCustomer &&
        (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName)))
    {
      throw new AppointmentBookingRuleException(
          "Podaj imię i nazwisko, aby zarezerwować wizytę.",
          ErrorCodes.AppointmentOtpMissingName);
    }

    // Kod OTP bramkuje przejście AwaitingOtp→Booked oraz mennicę zweryfikowanej sesji — musi pochodzić
    // z CSPRNG, nie z Random.Shared (xoshiro256**, przewidywalny). GetInt32 daje pełny zakres 000000–999999
    // (stary Next(100000, 999999) gubił zarówno kody z zerem wiodącym, jak i 999999).
    var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    var expiry = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(10);

    if (tenant.CustomerVerificationChannel == CustomerVerificationChannel.Phone)
    {
      if (string.IsNullOrWhiteSpace(request.PhoneNumber))
      {
        throw new AppointmentBookingRuleException(
            "Podaj numer telefonu w polu phoneNumber.",
            ErrorCodes.AppointmentOtpMissingContact);
      }

      var phone = new PhoneNumber(request.PhoneNumber!);
      // Anti toll-fraud: SMS tylko na numery polskie (+48). Numer premium/zagraniczny w publicznym
      // bookingu jest nielegitny dla ICP i służyłby drenażowi kredytów na drogie numery.
      if (!phone.IsPolish)
      {
        throw new AppointmentBookingRuleException(
            "Obsługujemy wyłącznie polskie numery telefonu (+48).",
            ErrorCodes.AppointmentOtpUnsupportedPhoneRegion);
      }
      await EnsureWhitelistOrThrow(tenant, appointment.TenantId, null, phone, cancellationToken);
      // Anti SMS-bomb: cap SMS-ów wysyłanych na docelowy numer NIEZALEŻNIE od appointmentu/IP.
      _otpProtection.AssertCanSendOtpToPhone(phone.Value);
      // [M1] Anti-rotacja-numeru: cap WYSŁANYCH OTP-SMS z tego IP/godzinę, niezależny od numeru.
      // Bez tego per-numer capy resetowały się z każdym nowym numerem (rotacja numeru z jednego IP).
      _otpProtection.AssertCanSendOtpSmsFromIp(clientIp);
      // Anti-abuse: twardy miesięczny limit SMS salonu. Po jego osiągnięciu blokujemy nawet OTP
      // (świadoma decyzja: ochrona kredytów ważniejsza niż dokończenie rezerwacji online).
      if (!await _usageGuard.IsWithinMonthlyCapAsync(appointment.TenantId, cancellationToken))
      {
        throw new SmsServiceUnavailableException(
            "Miesięczny limit SMS został wyczerpany — spróbuj później lub skontaktuj się z salonem.");
      }
      appointment.SetOtpVerification(OtpVerification.ForPhone(phone, code, expiry));
      await _phoneOtpSender.SendOtpAsync(phone.Value, code, cancellationToken);
      _otpProtection.RegisterOtpSentToPhone(phone.Value);
      // [M1] Bump per-IP/godzinę licznika wysłanych OTP-SMS — dopiero po udanej wysyłce.
      _otpProtection.RegisterOtpSmsSentFromIp(clientIp);
    }
    else
    {
      if (string.IsNullOrWhiteSpace(request.Email))
      {
        throw new AppointmentBookingRuleException(
            "Podaj adres e-mail w polu email.",
            ErrorCodes.AppointmentOtpMissingContact);
      }

      await EnsureWhitelistOrThrow(tenant, appointment.TenantId, request.Email, null, cancellationToken);
      // Anti email-bomb: cap maili wysyłanych na docelowy adres NIEZALEŻNIE od appointmentu/IP.
      // Jeden atakujący nie wyśle >5 maili/h na jeden adres, choćby stworzył 1000 appointmentów.
      _otpProtection.AssertCanSendOtpToEmail(request.Email!);
      // [M1e] Anti-rotacja-adresu: cap WYSŁANYCH OTP-maili z tego IP/godzinę, niezależny od adresu.
      // Cap per-adres wyżej resetuje się z każdym nowym adresem (catch-all `atak+N@domena`), a dla
      // kanału e-mail nie ma żadnego backstopu niżej — SmsApiClient dotyczy wyłącznie SMS.
      _otpProtection.AssertCanSendOtpEmailFromIp(clientIp);
      var otp = OtpVerification.ForEmail(request.Email!, code, expiry);
      appointment.SetOtpVerification(otp);
      await _emailSender.SendOtpCodeAsync(
        otp.Email!,
        code,
        EmailBrand.ForTenant(tenant.Name, tenant.BookingCalendarColorHex),
        cancellationToken);
      _otpProtection.RegisterOtpSentToEmail(request.Email!);
      // [M1e] Bump per-IP/godzinę licznika wysłanych OTP-maili — dopiero po udanej wysyłce.
      _otpProtection.RegisterOtpEmailSentFromIp(clientIp);
    }

    // User faktycznie zaczął flow OTP — wydłużamy lease, żeby zdążył odebrać kod
    // i wpisać 6 cyfr (initial HoldTtl 60s pokrywa tylko fill-form).
    appointment.ReplaceHoldLease(new HoldLease(
      appointment.Lease!.ReservationToken,
      _timeProvider.GetUtcNow().UtcDateTime.Add(_holdOptions.OtpLease)));

    await _uow.SaveChangesAsync(cancellationToken);
    _otpProtection.RegisterOtpRequestSucceeded(request.AppointmentId, clientIp);

    // OTP weryfikacji klienta to realna wysyłka (SMS = koszt) — liczymy ją do zużycia salonu.
    // Rejestrujemy po commitcie flow (osobny SaveChanges recordera), więc tylko udane OTP się liczą.
    if (tenant.CustomerVerificationChannel == CustomerVerificationChannel.Phone)
    {
      await _usageRecorder.RecordSmsAsync(
        appointment.TenantId, NotificationType.CustomerVerificationOtp, success: true, points: 0m, cancellationToken);
    }
    else
    {
      await _usageRecorder.RecordEmailAsync(
        appointment.TenantId, NotificationType.CustomerVerificationOtp, success: true, cancellationToken);
    }
  }

  /// <summary>
  /// Gdy salon ma `BookingAccessPolicy.InviteOnly`, kontakt z requesta musi pasować do
  /// klienta z `IsWhitelisted=true`. W trybie `Open` walidacja jest pomijana.
  /// </summary>
  private async Task EnsureWhitelistOrThrow(
    Tenant tenant,
    Guid tenantId,
    string? email,
    PhoneNumber? phone,
    CancellationToken ct)
  {
    if (tenant.BookingAccessPolicy != BookingAccessPolicy.InviteOnly)
    {
      return;
    }

    var query = _context.Customers.Where(c => c.TenantId == tenantId && c.IsWhitelisted);
    if (phone is not null)
    {
      // Równość po value-objekcie tłumaczy się przez value converter na porównanie kolumny
      // z literałem (indeks (TenantId, PhoneNumber)).
      var match = await query.AnyAsync(c => c.PhoneNumber == phone, ct);
      if (!match)
      {
        throw new BookingNotInvitedException();
      }
    }
    else if (!string.IsNullOrWhiteSpace(email))
    {
      var emailNorm = email.Trim().ToLowerInvariant();
      var match = await query.AnyAsync(c => c.Email != string.Empty && c.Email.ToLower() == emailNorm, ct);
      if (!match)
      {
        throw new BookingNotInvitedException();
      }
    }
    else
    {
      throw new BookingNotInvitedException();
    }
  }
}
