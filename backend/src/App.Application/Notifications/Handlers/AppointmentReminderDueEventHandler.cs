using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Wysyła klientowi przypomnienie o nadchodzącej wizycie — okno 24h lub ~2h zależnie od
/// <see cref="AppointmentReminderDueEvent.Is24h"/>. Każde okno ma osobne ustawienie tenanta.
/// </summary>
internal sealed class AppointmentReminderDueEventHandler
  : INotificationHandler<AppointmentReminderDueEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentReminderDueEventHandler> _logger;

  public AppointmentReminderDueEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentReminderDueEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentReminderDueEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      var type = e.Is24h
        ? NotificationType.AppointmentReminderToCustomer
        : NotificationType.AppointmentReminder2hToCustomer;

      var recipient = ctx.CustomerRecipient();
      if (!ctx.Tenant.NotificationSettings.IsEnabled(type) || recipient is null)
      {
        return;
      }

      var payload = new NotificationPayload(
        SalonName: ctx.SalonName,
        CustomerName: ctx.CustomerName,
        StaffName: ctx.StaffName,
        ServiceName: ctx.ServiceName,
        Date: ctx.Appointment.Date,
        StartTime: ctx.Appointment.StartTime);

      var window = e.Is24h ? "jutro" : "już wkrótce";

      await _dispatcher.DispatchAsync(new NotificationMessage(
        ctx.Tenant.Id,
        type,
        recipient,
        $"Przypomnienie o wizycie — {ctx.SalonName}",
        $"Przypominamy o wizycie {window} w {ctx.SalonName}: {ctx.ServiceName}, "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}.",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentReminderDueEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
