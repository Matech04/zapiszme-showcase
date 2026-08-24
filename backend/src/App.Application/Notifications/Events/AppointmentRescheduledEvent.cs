using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Wizyta została przełożona przez klienta (samoobsługa). Stary termin niesiony na zdarzeniu,
/// bo po zapisie encja ma już nowy. Publikowane z <c>RescheduleSelfServiceAppointmentCommandHandler</c>.
/// </summary>
public record AppointmentRescheduledEvent(
  Guid TenantId,
  Guid AppointmentId,
  DateOnly OldDate,
  TimeOnly OldStartTime) : INotification;
