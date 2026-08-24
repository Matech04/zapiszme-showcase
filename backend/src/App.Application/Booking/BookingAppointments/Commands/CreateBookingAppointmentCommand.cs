using App.Application.Appointments.Commands.PlaceAppointment;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Application.Booking.BookingAppointments.Commands;

/// <summary>
/// Rezerwacja z panelu klienta — tworzy wizytę ze statusem oczekującym i podaną dzierżawą slotu (HoldLease).
/// <paramref name="AnonSessionId"/> (cookie z controllera) służy do auto-anulowania poprzednich
/// aktywnych Pending appointments tego samego anonimowego użytkownika — chroni przed abuse
/// polegającym na blokowaniu wielu slotów przez F5 / wybieranie nowych slotów bez kończenia OTP.
/// </summary>
public record CreateBookingAppointmentCommand(
    Guid EmployeeId,
    IReadOnlyList<Guid> ServiceIds,
    DateOnly Date,
    TimeOnly StartTime,
    Guid? AnonSessionId = null)
    : IRequest<PublicBookingHoldDto>;

internal sealed class CreateBookingAppointmentCommandHandler : IRequestHandler<CreateBookingAppointmentCommand, PublicBookingHoldDto>
{
  private readonly ISender _mediator;
  private readonly IApplicationDbContext _context;
  private readonly ICurrentTenantService _currentTenant;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly BookingHoldOptions _holdOptions;
  private readonly TimeProvider _timeProvider;

  public CreateBookingAppointmentCommandHandler(
    ISender mediator,
    IApplicationDbContext context,
    ICurrentTenantService currentTenant,
    IBookingOtpProtection otpProtection,
    IHttpContextAccessor httpContextAccessor,
    IOptions<BookingHoldOptions> holdOptions,
    TimeProvider timeProvider)
  {
    _mediator = mediator;
    _context = context;
    _currentTenant = currentTenant;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
    _holdOptions = holdOptions.Value;
    _timeProvider = timeProvider;
  }

  public async Task<PublicBookingHoldDto> Handle(CreateBookingAppointmentCommand request, CancellationToken ct)
  {
    var tenantId = _currentTenant.TenantId
        ?? throw new NoTenantHeader();

    // [M4] Gate subskrypcji w write-flow holdu: nieaktywny salon (PastDue / Canceled / wygasły Trial)
    // nie może tworzyć rezerwacji online (fałszywa rezerwacja + późniejszy drenaż OTP-SMS). Read-query
    // GetPublicBookingSalon zwracał IsBookingAvailable=false, ale spreparowany klient mógł wołać /hold
    // bezpośrednio z pominięciem read.
    var tenant = await _context.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
        ?? throw new NotFoundException("Tenant", tenantId);
    var effectiveStatus = tenant.Subscription.EffectiveStatus;
    if (effectiveStatus != SubscriptionStatus.Trial && effectiveStatus != SubscriptionStatus.Active)
    {
      throw new BookingUnavailableException();
    }

    // Wstrzymane rezerwacje: salon ręcznie wstrzymał rezerwacje online (np. na czas zmian w grafiku).
    // Read-query GetPublicBookingSalon zwraca IsBookingPaused=true, ale spreparowany klient mógł wołać
    // /hold bezpośrednio z pominięciem read — dlatego gate egzekwujemy też tutaj, w write-flow.
    if (tenant.BookingPaused)
    {
      throw new BookingPausedException();
    }

    var clientIp = BookingClientIp.From(_httpContextAccessor.HttpContext);

    // Warstwa 2 anti-abuse: jeśli ta sama anonimowa sesja ma już aktywne holdy AwaitingOtp
    // (np. po F5 i wyborze nowego slotu), USUŃ je — uwalnia stare sloty. Każdy zwolniony hold
    // zwalnia też miejsce w per-IP liczniku [M3], więc legalny user (1 hold naraz) nie akumuluje się.
    if (request.AnonSessionId is { } anonSessionId)
    {
      await RemovePreviousHoldsForAnonSessionAsync(tenantId, anonSessionId, clientIp, ct);
    }

    // [M3] Twardy cap liczby JEDNOCZEŚNIE aktywnych holdów per IP — domyka slot hoarding przez rotację
    // AnonSessionId / brak cookie (auto-anulowanie działa tylko per-AnonSessionId). Rzuca po progu.
    _otpProtection.RegisterHoldCreatedForIp(clientIp);

    // Inkrement per-IP licznika holdów już nastąpił — jeśli DALSZE tworzenie holdu padnie (slot właśnie
    // zajęty, kolizja unique-index, past-date), MUSIMY zwolnić to miejsce w liczniku. Bez tego licznik
    // wyciekał do TTL i legalny klient trafiający w wyścig na popularny slot sam się blokował
    // („zbyt wiele rozpoczętych rezerwacji"), mimo że nie ma żadnego aktywnego holdu.
    try
    {
      var lease = new HoldLease(Guid.NewGuid(), _timeProvider.GetUtcNow().UtcDateTime.Add(_holdOptions.HoldTtl));

      // Inspiracje NIE są już dołączane przy holdzie (deferred-upload): zdjęcia trzymane są w przeglądarce
      // do potwierdzenia OTP i uploadowane dopiero potem (AttachInspirationImageCommand) — zero sierot.
      var appointmentId = await _mediator.Send(
          new PlaceAppointmentCommand(
              request.EmployeeId,
              request.ServiceIds,
              request.Date,
              request.StartTime,
              null,
              null,
              CreateAsBooked: false,
              CreateAsAwaitingOtp: true,
              HoldLease: lease,
              Source: AppointmentSource.Online),
          ct);

      // Wiążemy świeżo utworzony appointment z anonimową sesją — żeby kolejne /hold tej sesji
      // mogły go znaleźć i anulować, jeśli user nie skończy OTP.
      if (request.AnonSessionId is { } sessionId)
      {
        var appointment = await _context.Appointments.FirstAsync(a => a.Id == appointmentId, ct);
        appointment.SetAnonSession(sessionId);
        await _context.SaveChangesAsync(ct);
      }

      return new PublicBookingHoldDto(appointmentId, lease);
    }
    catch
    {
      _otpProtection.ReleaseHoldForIp(clientIp);
      throw;
    }
  }

  private async Task RemovePreviousHoldsForAnonSessionAsync(
    Guid tenantId, Guid anonSessionId, string? clientIp, CancellationToken ct)
  {
    var previous = await _context.Appointments
      .Where(a => a.TenantId == tenantId
               && a.AnonSessionId == anonSessionId
               && a.Status == AppointmentStatus.AwaitingOtp)
      .ToListAsync(ct);

    // Wszystkie `previous` to holdy AwaitingOtp — wizyty NIGDY niepotwierdzone (klient nie
    // przeszedł OTP). Kasujemy je twardo, analogicznie do joba cyklu życia, który porzucone
    // holdy z wygasłą dzierżawą usuwa (ExecuteDeleteAsync) — a nie zostawia jako Canceled.
    // Zostawianie ich jako Canceled tworzyło wieczne tombstone'y zaśmiecające widoki personelu
    // (licznik „zaplanowanych wizyt", sloty oznaczane jako zajęte). Realne anulowania
    // (potwierdzona wizyta → Canceled, zawsze Lease == null) NIE przechodzą tą ścieżką.
    if (previous.Count > 0)
    {
      _context.Appointments.RemoveRange(previous);

      // [M3] Zwolnij miejsce w per-IP liczniku aktywnych holdów — dzięki temu legalny user, który
      // wybiera kolejny slot (nowy /hold usuwa poprzedni), nie dobija do capu MaxConcurrentHoldsPerIp.
      foreach (var _ in previous)
      {
        _otpProtection.ReleaseHoldForIp(clientIp);
      }

      await _context.SaveChangesAsync(ct);
    }
  }

}
