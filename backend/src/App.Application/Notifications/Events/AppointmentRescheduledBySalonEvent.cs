using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Salon (panel) przełożył wizytę. Stary termin niesiony na zdarzeniu, bo po zapisie encja
/// ma już nowy. Publikowane z <c>RescheduleAppointmentHandler</c> tylko dla zmian z panelu
/// (ścieżka self-service publikuje własne <see cref="AppointmentRescheduledEvent"/>).
/// </summary>
public record AppointmentRescheduledBySalonEvent(
  Guid TenantId,
  Guid AppointmentId,
  DateOnly OldDate,
  TimeOnly OldStartTime) : INotification;
