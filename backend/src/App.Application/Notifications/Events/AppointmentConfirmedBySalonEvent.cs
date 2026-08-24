using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Salon ręcznie potwierdził wizytę (Pending → Booked). Publikowane z
/// <c>UpdateAppointmentStatusHandler</c>.
/// </summary>
public record AppointmentConfirmedBySalonEvent(Guid TenantId, Guid AppointmentId) : INotification;
