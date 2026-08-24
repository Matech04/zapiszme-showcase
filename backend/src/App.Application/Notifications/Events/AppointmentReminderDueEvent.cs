using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Nadszedł czas wysłać przypomnienie o nadchodzącej wizycie. <see cref="Is24h"/> = true dla
/// okna 24h, false dla okna ~2h. Publikowane z <c>AppointmentReminderHostedService</c>.
/// </summary>
public record AppointmentReminderDueEvent(
  Guid TenantId,
  Guid AppointmentId,
  bool Is24h) : INotification;
