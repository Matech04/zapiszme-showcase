using App.Application.Booking;
using App.Application.Booking.SelfService;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Booking.BookingAppointments.Commands;

public record VerifyOtpCommand(
  Guid Token,
  Guid AppointmentId,
  string Otp,
  string? FirstName = null,
  string? LastName = null,
  string? InstagramNick = null) : IRequest<VerifyOtpResult>;

/// <summary>
/// Wynik weryfikacji OTP. <see cref="RequiresManualConfirmation"/> = true, gdy salon działa w trybie
/// ręcznego potwierdzania (wizyta jest Pending i czeka na akceptację salonu) — front pokazuje wtedy
/// inny ekran sukcesu niż przy automatycznym potwierdzeniu.
/// </summary>
public record VerifyOtpResult(
  bool RequiresManualConfirmation,
  Guid? SessionToken = null,
  DateTime? SessionExpiresAtUtc = null,
  // Krótkożyjący token autoryzujący upload zdjęć inspiracji do tej (już potwierdzonej) wizyty.
  // Zdjęcia trzymane w przeglądarce do tej chwili — upload PO confirm (zero sierot na storage).
  string? InspirationUploadToken = null);

internal sealed class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, VerifyOtpResult>
{
  private readonly IAppointmentRepository _appointmentRepository;
  private readonly ICustomerRepository _customerRepository;
  private readonly ITenantRepository _tenantRepository;
  private readonly IApplicationDbContext _context;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly IUnitOfWork _uow;
  private readonly IPublisher _publisher;
  private readonly ILogger<VerifyOtpCommandHandler> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly ICurrentTenantService _currentTenant;
  private readonly IInspirationUploadTokenService _inspirationTokens;

  public VerifyOtpCommandHandler(
    IAppointmentRepository appointmentRepository,
    ICustomerRepository customerRepository,
    ITenantRepository tenantRepository,
    IApplicationDbContext context,
    IBookingOtpProtection otpProtection,
    IHttpContextAccessor httpContextAccessor,
    IUnitOfWork uow,
    IPublisher publisher,
    ILogger<VerifyOtpCommandHandler> logger,
    TimeProvider timeProvider,
    ICurrentTenantService currentTenant,
    IInspirationUploadTokenService inspirationTokens)
  {
    _appointmentRepository = appointmentRepository;
    _customerRepository = customerRepository;
    _tenantRepository = tenantRepository;
    _inspirationTokens = inspirationTokens;
    _context = context;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _uow = uow;
    _publisher = publisher;
    _logger = logger;
    _timeProvider = timeProvider;
    _currentTenant = currentTenant;
  }

  public async Task<VerifyOtpResult> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
  {
    var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Defense-in-depth: wizyta MUSI należeć do tenanta ze slugu (ustawionego przez middleware).
    // Query-filter w GetByIdAsync już to gwarantuje; jawny check chroni przed regresją (gdyby ktoś
    // dodał IgnoreQueryFilters). 404 jak przy braku — bez ujawniania istnienia cudzej wizyty.
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

    if (appointment.OtpVerification is null)
    {
      throw new AppointmentBookingRuleException(
          "Najpierw poproś o kod weryfikacyjny (request-otp).",
          ErrorCodes.AppointmentOtpVerificationRequired);
    }

    var clientIp = BookingClientIp.From(_httpContextAccessor.HttpContext);
    _otpProtection.RecordVerifyOtpAttempt(clientIp);

    // Cel weryfikacji (numer/email) — per-target licznik nieudanych prób domyka brute-force
    // amortyzowany rotacją holdu (nowy appointmentId resetuje per-appointment budżet 3 prób).
    var otpTarget = appointment.OtpVerification.Channel == OtpVerificationChannel.Phone
        ? appointment.OtpVerification.PhoneE164
        : appointment.OtpVerification.Email;

    if (_otpProtection.IsVerificationBlocked(appointment.Id)
        || (!string.IsNullOrWhiteSpace(otpTarget) && _otpProtection.IsTargetVerificationBlocked(otpTarget!)))
    {
      throw new AppointmentBookingRuleException(
          "Wykorzystano 3 nieudane próby wpisania kodu. Kliknij „Wyślij kod”, aby otrzymać nowy.",
          ErrorCodes.AppointmentOtpTooManyFailures);
    }

    if (!appointment.OtpVerification.IsValid(request.Otp, _timeProvider.GetUtcNow().UtcDateTime))
    {
      var fails = _otpProtection.RegisterFailedVerificationAttempt(appointment.Id);
      if (!string.IsNullOrWhiteSpace(otpTarget))
      {
        _otpProtection.RegisterFailedVerificationForTarget(otpTarget!);
      }
      if (fails >= 3)
      {
        throw new AppointmentBookingRuleException(
            "3 razy podano błędny lub wygasły kod. Poproś o nowy kod OTP (przycisk „Wyślij kod”).",
            ErrorCodes.AppointmentOtpTooManyFailures);
      }

      throw new AppointmentBookingRuleException(
          $"Nieprawidłowy lub wygasły kod OTP. Pozostało prób: {3 - fails}.",
          ErrorCodes.AppointmentOtpInvalidCode);
    }

    _otpProtection.ClearVerificationAttempts(appointment.Id);
    if (!string.IsNullOrWhiteSpace(otpTarget))
    {
      _otpProtection.ClearTargetVerificationAttempts(otpTarget!);
    }

    // [M4] Ostatnia bramka przed wpisaniem wizyty do kalendarza. Capy wyżej ograniczają koszt WYSYŁKI
    // kodów, ale nie liczbę powstałych wizyt — a rotacja adresu e-mail (catch-all) pozwalała odebrać
    // dowolnie wiele kodów. Dodatkowo ReleaseHoldForIp niżej zwalnia miejsce w liczniku holdów, więc
    // MaxConcurrentHoldsPerIp też tego nie łapił. Po weryfikacji (nieudane próby nie palą budżetu),
    // przed zapisem — żeby odrzucone potwierdzenie nie zostawiło wizyty.
    _otpProtection.AssertCanConfirmBookingFromIp(clientIp);

    var tenant = await _tenantRepository.GetByIdAsync(appointment.TenantId);

    // Imię/nazwisko wymagane przez salon. „Umawiam ponownie": pozwalamy je pominąć tylko gdy
    // zweryfikowany OTP-em kontakt pasuje do ISTNIEJĄCEGO klienta z uzupełnionym imieniem i nazwiskiem
    // (dane bierze z jego rekordu BookingCustomerLinker — nie nadpisuje). Lookup PO weryfikacji OTP,
    // więc nie zdradza istnienia klienta bez udowodnienia władania kontaktem (anti-enumeracja).
    if (tenant?.RequireCustomerName == true &&
        (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName)))
    {
      var ov = appointment.OtpVerification;
      Customer? known = null;
      if (ov.Channel == OtpVerificationChannel.Phone && !string.IsNullOrWhiteSpace(ov.PhoneE164))
      {
        known = await _customerRepository.GetByPhoneNumber(
            appointment.TenantId, new PhoneNumber(ov.PhoneE164!), cancellationToken);
      }
      else if (ov.Channel == OtpVerificationChannel.Email && !string.IsNullOrWhiteSpace(ov.Email))
      {
        known = await _customerRepository.GetByEmail(appointment.TenantId, ov.Email!, cancellationToken);
      }

      if (known is null ||
          string.IsNullOrWhiteSpace(known.FirstName) ||
          string.IsNullOrWhiteSpace(known.LastName))
      {
        // Ten sam, generyczny komunikat co przy zwykłym braku imienia — NIE zdradzamy, czy podany
        // kontakt jest już klientem salonu (privacy-oracle). Klient po prostu podaje imię i nazwisko.
        throw new AppointmentBookingRuleException(
            "Podaj imię i nazwisko, aby potwierdzić wizytę.",
            ErrorCodes.AppointmentOtpMissingName);
      }
    }

    var confirmedStatus = tenant?.AppointmentConfirmationMode == AppointmentConfirmationMode.Manual
      ? AppointmentStatus.Pending
      : AppointmentStatus.Booked;
    appointment.ChangeStatus(confirmedStatus);

    // Po potwierdzeniu wizyta nie jest już „holdem" w lejku — czyścimy dzierżawę, inaczej
    // job cyklu życia (AppointmentStatusLifecycleHostedService) skasowałby z bazy potwierdzoną
    // wizytę Pending (tryb ręczny) po wygaśnięciu starej dzierżawy OTP.
    appointment.ClearHoldLease();

    // Po pomyślnej weryfikacji OTP — gość staje się klientem CRM tenanta (create/enrich).
    var verification = appointment.OtpVerification;
    await BookingCustomerLinker.LinkCustomerAsync(
      appointment,
      _customerRepository,
      verification.Channel,
      verification.Email,
      verification.PhoneE164,
      request.FirstName,
      request.LastName,
      request.InstagramNick,
      cancellationToken);

    // Mennica sesji zweryfikowanego kontaktu: klient właśnie udowodnił władanie kontaktem przez OTP,
    // więc wystawiamy token sesji (jak po logowaniu w panelu). Front trzyma go in-session i może nim
    // pominąć OTP przy KOLEJNEJ rezerwacji na ten sam kontakt (oraz wejść do panelu zarządzania).
    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
    var session = SelfServiceOtp.CreateVerifiedSession(
      appointment.TenantId,
      verification.Channel == OtpVerificationChannel.Email ? verification.Email : null,
      verification.Channel == OtpVerificationChannel.Phone ? verification.PhoneE164 : null,
      nowUtc,
      SelfServiceSessionPolicy.VerifiedSessionCodeRetention,
      SelfServiceSessionPolicy.SessionLifetime);
    await _context.SelfServiceOtps.AddAsync(session, cancellationToken);

    try
    {
      await _uow.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      // Wiersz usunięty przez background job (wygasły hold) między załadowaniem a zapisem.
      // Traktujemy jak brak wizyty — klient zobaczy komunikat o wygaśnięciu sesji.
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    // [M3] Hold przestał istnieć (ClearHoldLease wyżej) — zwolnij miejsce w per-IP liczniku aktywnych
    // holdów. Bez tego licznik trzymał POTWIERDZONE wizyty przez cały TTL klucza, więc mierzył
    // „holdy utworzone w ostatnich ~10 min", a nie „aktywne teraz": 5 udanych rezerwacji z jednego
    // adresu (CGNAT operatora, firmowe/hotelowe NAT) blokowało szóstą. Po SaveChangesAsync, żeby
    // nieudany zapis nie zwolnił miejsca dla rezerwacji, która nigdy nie powstała.
    _otpProtection.ReleaseHoldForIp(clientIp);

    // Powiadomienia best-effort — błąd publikacji nie może wywrócić potwierdzonej rezerwacji.
    // Tryb Automatic → rezerwacja od razu Booked; tryb Manual → Pending i czeka na salon.
    try
    {
      INotification notification = confirmedStatus == AppointmentStatus.Booked
        ? new BookingConfirmedEvent(appointment.TenantId, appointment.Id)
        : new BookingAwaitingConfirmationEvent(appointment.TenantId, appointment.Id);
      await _publisher.Publish(notification, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja zdarzenia powiadomień dla wizyty {Id} nie powiodła się", appointment.Id);
    }

    return new VerifyOtpResult(
      confirmedStatus == AppointmentStatus.Pending,
      session.SessionToken,
      session.SessionExpiresAtUtc,
      // Wizyta potwierdzona → wydaj token autoryzujący upload zdjęć inspiracji (front uploaduje po confirm).
      _inspirationTokens.Issue(appointment.Id));
  }
}
