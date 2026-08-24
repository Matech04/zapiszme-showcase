using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Wizyta została anulowana przez klienta (samoobsługa). Publikowane po zapisie z
/// <c>CancelSelfServiceAppointmentCommandHandler</c>.
/// </summary>
public record AppointmentCancelledEvent(Guid TenantId, Guid AppointmentId) : INotification;
