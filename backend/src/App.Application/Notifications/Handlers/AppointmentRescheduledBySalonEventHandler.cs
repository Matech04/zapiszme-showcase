using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Salon (panel) przełożył wizytę — powiadamia klienta o nowym terminie.
/// </summary>
internal sealed class AppointmentRescheduledBySalonEventHandler
  : INotificationHandler<AppointmentRescheduledBySalonEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentRescheduledBySalonEventHandler> _logger;

  public AppointmentRescheduledBySalonEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentRescheduledBySalonEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentRescheduledBySalonEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      var recipient = ctx.CustomerRecipient();
      if (!ctx.Tenant.NotificationSettings.IsEnabled(NotificationType.RescheduledBySalonToCustomer)
          || recipient is null)
      {
        return;
      }

      var payload = new NotificationPayload(
        SalonName: ctx.SalonName,
        CustomerName: ctx.CustomerName,
        StaffName: ctx.StaffName,
        ServiceName: ctx.ServiceName,
        Date: ctx.Appointment.Date,
        StartTime: ctx.Appointment.StartTime,
        OldDate: e.OldDate,
        OldStartTime: e.OldStartTime);

      await _dispatcher.DispatchAsync(new NotificationMessage(
        ctx.Tenant.Id,
        NotificationType.RescheduledBySalonToCustomer,
        recipient,
        $"Zmiana terminu wizyty — {ctx.SalonName}",
        $"Salon {ctx.SalonName} przełożył Twoją wizytę ({ctx.ServiceName}) na "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}.",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentRescheduledBySalonEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
