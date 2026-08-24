using App.Application.Booking;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace App.Application.Auth.Commands.SendPhoneOtp;

/// <summary>
/// Wewnętrzny use-case: generuje 6-cyfrowy kod, invaliduje poprzedni aktywny OTP usera,
/// zapisuje nowy, wysyła SMS przez <see cref="IPhoneOtpSender"/>. Wywoływany z handlera
/// <c>ConfirmEmail</c> (pierwszy SMS po potwierdzeniu maila) i z <c>ResendPhoneOtp</c>.
/// </summary>
public sealed record SendPhoneOtpCommand(Guid UserId) : IRequest;

internal sealed class SendPhoneOtpCommandHandler : IRequestHandler<SendPhoneOtpCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IPhoneOtpSender _sender;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _ttl;
  private readonly ILogger<SendPhoneOtpCommandHandler> _logger;
  // Opcjonalny (param na końcu, default null) — DI wstrzyknie w prod; testy tworzące handler wprost
  // nie wymagają argumentu (clientIp → null = no-op per IBookingOtpProtection).
  private readonly IHttpContextAccessor? _httpContextAccessor;

  // Dev-only stały kod OTP; null = zachowanie produkcyjne (losowy). O aktywności decyduje
  // Program.cs (gate na IsDevelopment) — patrz PhoneOtpDevOptions.
  private readonly string? _devFixedCode;

  public SendPhoneOtpCommandHandler(
    IApplicationDbContext context,
    IPhoneOtpSender sender,
    IBookingOtpProtection otpProtection,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<SendPhoneOtpCommandHandler> logger,
    IHttpContextAccessor? httpContextAccessor = null,
    PhoneOtpDevOptions? devOptions = null)
  {
    _context = context;
    _sender = sender;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _timeProvider = timeProvider;
    _logger = logger;
    _devFixedCode = devOptions?.FixedCode;
    var ttlMinutes = configuration.GetValue<double?>("Auth:PhoneOtpTtlMinutes") ?? 10d;
    _ttl = TimeSpan.FromMinutes(ttlMinutes);
  }

  public async Task Handle(SendPhoneOtpCommand request, CancellationToken ct)
  {
    var user = await _context.Users
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
      ?? throw new NotFoundException("User", request.UserId);

    if (string.IsNullOrWhiteSpace(user.PhoneNumber))
    {
      throw new InvalidOperationException(
        $"Użytkownik {request.UserId} nie ma numeru telefonu — nie można wysłać OTP.");
    }

    // Anti SMS-bomb: cap SMS-ów na docelowy numer (ten sam mechanizm co publiczny OTP klienta).
    // Ścieżka rejestracji jest pre-tenant, więc to — obok globalnego dziennego capa w SmsApiClient
    // i +48 z register-owner — jedyna ochrona per-numer przed drenażem przez resend.
    _otpProtection.AssertCanSendOtpToPhone(user.PhoneNumber!);
    // [M1] Cap WYSŁANYCH OTP-SMS z tego IP/godzinę, niezależny od numeru. Ścieżka rejestracji jest
    // pre-tenant (brak miesięcznego capa salonu), więc per-IP domyka rotację numerów przez wiele
    // rejestracji / resend z jednego IP. Brak HttpContext → no-op.
    var clientIp = BookingClientIp.From(_httpContextAccessor?.HttpContext);
    _otpProtection.AssertCanSendOtpSmsFromIp(clientIp);

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    // Invalidate any active OTP for this user (soft consume).
    var activeOtps = await _context.PhoneConfirmationOtps
      .Where(o => o.UserId == request.UserId && o.ConsumedAt == null)
      .ToListAsync(ct);
    foreach (var prev in activeOtps)
    {
      prev.Consume(nowUtc);
    }

    // Stały kod tylko w dev (patrz PhoneOtpDevOptions). Dalej wszystko idzie identycznie jak w prod:
    // ten sam hash, ten sam zapis, ta sama weryfikacja — różni się WYŁĄCZNIE źródło losowości.
    //
    // CSPRNG, nie Random.Shared (xoshiro256**, przewidywalny) — ten kod bramkuje aktywację konta
    // salonu, a przejęcie konta to dostęp do CAŁEJ bazy klientek tenanta. Dwie pozostałe ścieżki OTP
    // przeniesiono wcześniej, ta została pominięta (preflight 2026-07-31).
    //
    // GetInt32(0, 1_000_000) daje pełny zakres 000000–999999. Poprzednie Next(100000, 999999)
    // gubiło 999999 (górna granica wyłączna) i wszystkie kody z zerem wiodącym — przestrzeń
    // ~900 tys. zamiast miliona.
    var code = _devFixedCode ?? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    var hash = OtpCodeHasher.Hash(code);
    var otp = PhoneConfirmationOtp.Create(request.UserId, hash, nowUtc, _ttl);
    _context.PhoneConfirmationOtps.Add(otp);
    await _context.SaveChangesAsync(ct);

    // Send AFTER persist — jeśli wysyłka padnie, OTP istnieje (user może użyć resend),
    // ale w transakcji integralność jest zachowana. SmsServiceUnavailableException propaguje do callera.
    await _sender.SendOtpAsync(user.PhoneNumber!, code, ct);
    _otpProtection.RegisterOtpSentToPhone(user.PhoneNumber!);
    _otpProtection.RegisterOtpSmsSentFromIp(clientIp);

    _logger.LogInformation(
      "PhoneOtpSent UserId={UserId} OtpId={OtpId} Category={Category}",
      request.UserId, otp.Id, "audit");
  }
}
