using App.Application.Booking;
using App.Application.Booking.SelfService;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Booking.BookingAppointments.Commands;

/// <summary>
/// Potwierdza świeży hold rezerwacji BEZ wpisywania kodu OTP, wykorzystując ważną sesję zweryfikowanego
/// kontaktu (token z <c>verify-otp</c> / panelu). Kontakt klienta bierzemy z sesji (authoritative).
/// Sama komenda NIE wysyła OTP, ale potwierdzenie publikuje <c>BookingConfirmedEvent</c>, które może
/// wysłać płatny SMS potwierdzający — dlatego objęte jest osobnym capem
/// (<see cref="IBookingOtpProtection.AssertCanConfirmWithSession"/>), by jedna sesja nie drenowała
/// budżetu SMS. Egzekwujemy też serwerowo: świeży hold (AwaitingOtp), zgodność kontaktu sesji z holdem
/// (gdy hold ma już OtpVerification) oraz polski numer (+48). Izolacja tenanta i bramka subskrypcji jak wyżej.
/// </summary>
public record ConfirmBookingWithSessionCommand(
  string Slug,
  Guid AppointmentId,
  Guid Token,
  Guid SessionToken,
  string? FirstName = null,
  string? LastName = null,
  string? InstagramNick = null) : IRequest<VerifyOtpResult>;

internal sealed class ConfirmBookingWithSessionCommandHandler
    : IRequestHandler<ConfirmBookingWithSessionCommand, VerifyOtpResult>
{
  private readonly IAppointmentRepository _appointmentRepository;
  private readonly ICustomerRepository _customerRepository;
  private readonly IApplicationDbContext _context;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly IUnitOfWork _uow;
  private readonly IPublisher _publisher;
  private readonly ILogger<ConfirmBookingWithSessionCommandHandler> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly ICurrentTenantService _currentTenant;
  private readonly IInspirationUploadTokenService _inspirationTokens;

  public ConfirmBookingWithSessionCommandHandler(
    IAppointmentRepository appointmentRepository,
    ICustomerRepository customerRepository,
    IApplicationDbContext context,
    IBookingOtpProtection otpProtection,
    IHttpContextAccessor httpContextAccessor,
    IUnitOfWork uow,
    IPublisher publisher,
    ILogger<ConfirmBookingWithSessionCommandHandler> logger,
    TimeProvider timeProvider,
    ICurrentTenantService currentTenant,
    IInspirationUploadTokenService inspirationTokens)
  {
    _appointmentRepository = appointmentRepository;
    _customerRepository = customerRepository;
    _context = context;
    _inspirationTokens = inspirationTokens;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _uow = uow;
    _publisher = publisher;
    _logger = logger;
    _timeProvider = timeProvider;
    _currentTenant = currentTenant;
  }

  public async Task<VerifyOtpResult> Handle(ConfirmBookingWithSessionCommand request, CancellationToken cancellationToken)
  {
    var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Defense-in-depth: wizyta MUSI należeć do tenanta ze slugu (jak w VerifyOtpCommand).
    if (_currentTenant.TenantId is { } currentTenantId && appointment.TenantId != currentTenantId)
    {
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    // Ważny hold (status AwaitingOtp niesie lease). Brak/niepoprawny token → 403.
    if (appointment.Lease is null || !appointment.Lease.IsValid(request.Token))
    {
      throw new ForbiddenAccessException(
          "Nieprawidłowy lub wygasły token rezerwacji.",
          ErrorCodes.AppointmentOtpInvalidLease);
    }

    // Tylko świeży hold (nigdy nieprzetworzona rezerwacja) — nie wolno tą ścieżką ruszać/nadpisywać
    // wizyty już potwierdzonej ani powiązanej z innym kontaktem. W praktyce tylko AwaitingOtp niesie lease.
    if (appointment.Status != AppointmentStatus.AwaitingOtp)
    {
      throw new ForbiddenAccessException(
          "Rezerwacji nie można potwierdzić w tym stanie.",
          ErrorCodes.AppointmentOtpInvalidLease);
    }

    // Sesja zweryfikowanego kontaktu (Consumed + nieprzeterminowana). Rzuca 403 gdy nieważna/wygasła.
    var session = await SelfServiceSessionResolver.ResolveAsync(
      _context, request.Slug, request.SessionToken, _timeProvider, cancellationToken);

    // Token sesji i wizyta muszą należeć do tego samego tenanta (resolver skanuje po slug→tenant,
    // wizyta jest sprawdzona powyżej — jawny guard chroni przed regresją).
    if (session.TenantId != appointment.TenantId)
    {
      throw new ForbiddenAccessException(
          "Sesja nie pasuje do tej rezerwacji.",
          ErrorCodes.AppointmentSessionContactMismatch);
    }

    // Kontakt klienta wyłącznie z sesji (authoritative). Mennica sesji gwarantuje dokładnie jeden z pól.
    var channel = session.Email is not null
      ? OtpVerificationChannel.Email
      : OtpVerificationChannel.Phone;

    // [H2] Jeśli hold przeszedł już przez request-otp (ma OtpVerification), kontakt sesji MUSI się
    // z nim zgadzać — inaczej cudza sesja mogłaby przejąć hold założony dla innego kontaktu.
    if (appointment.OtpVerification is { } pending)
    {
      var contactMatches = pending.Channel == channel && (channel == OtpVerificationChannel.Email
        ? string.Equals(pending.Email, session.Email, StringComparison.OrdinalIgnoreCase)
        : string.Equals(pending.PhoneE164, session.PhoneE164, StringComparison.Ordinal));
      if (!contactMatches)
      {
        throw new ForbiddenAccessException(
            "Kontakt sesji nie pasuje do tej rezerwacji.",
            ErrorCodes.AppointmentSessionContactMismatch);
      }
    }

    // [M2] Anti toll-fraud: potwierdzenie wyśle SMS potwierdzający — tylko polskie numery (+48), jak request-otp.
    if (channel == OtpVerificationChannel.Phone && !new PhoneNumber(session.PhoneE164!).IsPolish)
    {
      throw new AppointmentBookingRuleException(
          "Obsługujemy wyłącznie polskie numery telefonu (+48).",
          ErrorCodes.AppointmentOtpUnsupportedPhoneRegion);
    }

    var tenant = await _context.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == appointment.TenantId, cancellationToken)
        ?? throw new NotFoundException(nameof(Tenant), appointment.TenantId);

    // [M4] Bramka subskrypcji w write-flow: nieaktywny salon nie może potwierdzać rezerwacji.
    var effectiveStatus = tenant.Subscription.EffectiveStatus;
    if (effectiveStatus != SubscriptionStatus.Trial && effectiveStatus != SubscriptionStatus.Active)
    {
      throw new BookingUnavailableException();
    }

    if (tenant.RequireCustomerName &&
        (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName)))
    {
      throw new AppointmentBookingRuleException(
          "Podaj imię i nazwisko, aby potwierdzić wizytę.",
          ErrorCodes.AppointmentOtpMissingName);
    }

    // [H1] Cap potwierdzeń per sesja + per IP — potwierdzenie wyzwala płatny SMS poza capami OTP.
    // Atomowa rezerwacja slotu (lock w serwisie) tuż przed potwierdzeniem, PO pozostałych bramkach
    // (subskrypcja/imię), by odrzucone żądania nie konsumowały budżetu. Brak osobnego „register" —
    // rezerwacja i sprawdzenie są jedną operacją, co eliminuje TOCTOU przy równoległych żądaniach.
    var clientIp = BookingClientIp.From(_httpContextAccessor.HttpContext);
    _otpProtection.AssertCanConfirmWithSession(request.SessionToken, clientIp);

    var confirmedStatus = tenant.AppointmentConfirmationMode == AppointmentConfirmationMode.Manual
      ? AppointmentStatus.Pending
      : AppointmentStatus.Booked;
    appointment.ChangeStatus(confirmedStatus);
    appointment.ClearHoldLease();

    await BookingCustomerLinker.LinkCustomerAsync(
      appointment,
      _customerRepository,
      channel,
      session.Email,
      session.PhoneE164,
      request.FirstName,
      request.LastName,
      request.InstagramNick,
      cancellationToken);

    try
    {
      await _uow.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    // [M3] Jak w VerifyOtpCommand: hold zamienił się w potwierdzoną wizytę (ClearHoldLease wyżej),
    // więc nie może dalej zajmować miejsca w per-IP liczniku aktywnych holdów.
    _otpProtection.ReleaseHoldForIp(clientIp);

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

    // Sesja już istnieje (przekazana przez klienta) — nie wystawiamy nowej.
    return new VerifyOtpResult(
      confirmedStatus == AppointmentStatus.Pending,
      // Wizyta potwierdzona → token do uploadu zdjęć inspiracji (front uploaduje po confirm).
      InspirationUploadToken: _inspirationTokens.Issue(appointment.Id));
  }
}
