using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Salon odwołał wizytę (zmiana statusu na Canceled lub usunięcie). <see cref="WasPending"/>
/// rozróżnia odrzucenie rezerwacji oczekującej od odwołania potwierdzonej wizyty (treść maila).
/// </summary>
public record AppointmentCancelledBySalonEvent(
  Guid TenantId,
  Guid AppointmentId,
  bool WasPending) : INotification;
