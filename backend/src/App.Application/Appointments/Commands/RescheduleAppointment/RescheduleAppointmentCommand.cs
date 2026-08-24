using App.Application.Appointments.Commands.ApplyReschedule;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using MediatR;

namespace App.Application.Appointments.Commands.RescheduleAppointment;

public record RescheduleAppointmentCommand(
  Guid AppointmentId,
  Guid EmployeeId,
  IReadOnlyList<Guid> ServiceIds,
  DateOnly Date,
  TimeOnly StartTime,
  bool IsSelfService = false,
  // Zmiana terminu „poza grafikiem" z panelu — pomija godziny pracy/urlop, blokuje tylko kolizja.
  // Ścieżka self-service klienta tego nie ustawia (zostaje false).
  bool IgnoreSchedule = false,
  // Egzekwuje widoczność terminu dla KLIENTA (horyzont rezerwacji + publikacja miesiąca). Ustawiane
  // przez ścieżki klienta: self-service oraz PATCH holdu z publicznego bookingu. Panel przekłada
  // wizyty bez ograniczenia. Świadomie osobny znacznik, a nie IsSelfService — tamten steruje
  // wyłącznie publikacją powiadomienia i PATCH holdu ma go na false.
  bool EnforcePublicVisibility = false,
  // Niestandardowy czas trwania wizyty (minuty). Null = zachowaj bieżący override (samo przesunięcie
  // terminu nie kasuje niestandardowego bloku); wartość = ustaw nowy czas przy zmianie terminu.
  int? CustomDurationMinutes = null
  ) : IRequest<Guid>;

/// <summary>
/// Wejście PERSONELU. Autoryzuje kalendarz pracownika, NA KTÓREGO wizyta trafia, po czym oddaje
/// robotę wspólnemu <c>ApplyRescheduleCommand</c>.
/// </summary>
internal class RescheduleAppointmentHandler : TenantHandler<RescheduleAppointmentCommand, Guid>
{
  private readonly IStaffAccessPolicy _access;
  private readonly IMediator _mediator;

  public RescheduleAppointmentHandler(
    IStaffAccessPolicy access,
    IMediator mediator,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _access = access;
    _mediator = mediator;
  }

  public override async Task<Guid> Handle(RescheduleAppointmentCommand request, CancellationToken ct)
  {
    // Reschedule rusza DWA kalendarze, więc oba muszą być w zasięgu wołającego.
    //
    // ŹRÓDŁO — wizyta, którą zabieramy. Bez tego pracownik z `OwnCalendarOnly` podawał
    // `AppointmentId` kolegi i własne `EmployeeId`: cel = on sam, bramka przepuszczała, a rdzeń
    // przepinał wizytę na jego kalendarz. Kolega tracił klientkę. Reszta mutacji (status, notatka,
    // cena, usługi, delete) autoryzuje `appointment.EmployeeId`; reschedule był jedynym wyjątkiem.
    await _access.EnsureCanMutateAppointmentAsync(request.AppointmentId, ct);

    // CEL — kalendarz, na który wizyta trafia.
    await _access.EnsureCanMutateEmployeeCalendarAsync(request.EmployeeId, ct);

    return await _mediator.Send(
      new ApplyRescheduleCommand(
        request.AppointmentId,
        request.EmployeeId,
        request.ServiceIds,
        request.Date,
        request.StartTime,
        request.IsSelfService,
        request.IgnoreSchedule,
        request.EnforcePublicVisibility,
        request.CustomDurationMinutes),
      ct);
  }
}
