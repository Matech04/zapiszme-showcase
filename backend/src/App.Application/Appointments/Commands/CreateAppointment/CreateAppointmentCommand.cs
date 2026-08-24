using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Appointments.Commands.PlaceAppointment;
using App.Domain.Aggregates.AppointmentAggregate;
using MediatR;

namespace App.Application.Appointments.Commands.CreateAppointment;

public record CreateAppointmentCommand(
  Guid EmployeeId,
  IReadOnlyList<Guid> ServiceIds,
  DateOnly Date,
  TimeOnly StartTime,
  Guid? CustomerId,
  string? CustomerPhone,
  bool CreateAsBooked = false,
  bool CreateAsAwaitingOtp = false,
  bool CreateAsCompleted = false,
  HoldLease? HoldLease = null,
  AppointmentSource Source = AppointmentSource.Panel,
  // Zapis „poza grafikiem": personel świadomie pomija godziny pracy/urlop — blokuje tylko
  // kolizja z inną wizytą. Honorowane wyłącznie dla Source = Panel (patrz handler).
  bool IgnoreSchedule = false,
  // Zdjęcia inspiracji (≤3) dołączone przez klientkę w publicznym bookingu — już przetworzone
  // i wgrane przez anonimowy endpoint uploadu. Cap egzekwuje agregat (SetInspirationImages).
  IReadOnlyList<AppointmentInspirationLine>? InspirationImages = null,
  // Niestandardowy czas trwania wizyty (minuty) ustawiony przez personel — nadpisuje długość bloku
  // w kalendarzu. Null = czas standardowy (suma czasów usług). Rezerwacja online tego nie podaje.
  int? CustomDurationMinutes = null) : IRequest<Guid>;

/// <summary>
/// Wejście PERSONELU. Autoryzuje kalendarz docelowego pracownika (rola + StaffCalendarVisibilityPolicy),
/// po czym oddaje robotę wspólnemu <c>PlaceAppointmentCommand</c>. Publiczny booking omija tę osłonę —
/// ma własną autoryzację (slug→tenant, hold lease, OTP) i żadnego zalogowanego pracownika.
///
/// Ten rekord CELOWO nie implementuje <c>IAppointmentWriteRequest</c> — pauza rezerwacji wisi na
/// rdzeniu, przez który przechodzą obie ścieżki. Gdyby siedziała też tutaj, zapis z panelu czytałby
/// tabelę Tenants dwa razy na jedną wizytę.
/// </summary>
internal class CreateAppointmentHandler : TenantHandler<CreateAppointmentCommand, Guid>
{
  private readonly IStaffAccessPolicy _access;
  private readonly IMediator _mediator;

  public CreateAppointmentHandler(
    IStaffAccessPolicy access,
    IMediator mediator,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _access = access;
    _mediator = mediator;
  }

  public override async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken ct)
  {
    // Pracownik, NA KTÓREGO wizyta trafia — odwzorowuje dawne `CanMutateEmployeeSchedule` w kontrolerze.
    await _access.EnsureCanMutateEmployeeCalendarAsync(request.EmployeeId, ct);

    return await _mediator.Send(
      new PlaceAppointmentCommand(
        request.EmployeeId,
        request.ServiceIds,
        request.Date,
        request.StartTime,
        request.CustomerId,
        request.CustomerPhone,
        request.CreateAsBooked,
        request.CreateAsAwaitingOtp,
        request.CreateAsCompleted,
        request.HoldLease,
        request.Source,
        request.IgnoreSchedule,
        request.InspirationImages,
        request.CustomDurationMinutes),
      ct);
  }
}
