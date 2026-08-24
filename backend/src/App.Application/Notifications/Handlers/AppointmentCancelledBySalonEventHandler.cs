using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Salon odwołał wizytę — powiadamia klienta. Treść rozróżnia odrzucenie rezerwacji
/// oczekującej (<c>WasPending</c>) od odwołania potwierdzonej wizyty.
/// </summary>
internal sealed class AppointmentCancelledBySalonEventHandler
  : INotificationHandler<AppointmentCancelledBySalonEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentCancelledBySalonEventHandler> _logger;

  public AppointmentCancelledBySalonEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentCancelledBySalonEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentCancelledBySalonEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      var recipient = ctx.CustomerRecipient();
      if (!ctx.Tenant.NotificationSettings.IsEnabled(NotificationType.CancelledBySalonToCustomer)
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
        StartTime: ctx.Appointment.StartTime);

      var noun = e.WasPending ? "Rezerwacja odrzucona" : "Wizyta odwołana";
      var verb = e.WasPending ? "odrzucona" : "odwołana";

      await _dispatcher.DispatchAsync(new NotificationMessage(
        ctx.Tenant.Id,
        NotificationType.CancelledBySalonToCustomer,
        recipient,
        $"{noun} — {ctx.SalonName}",
        $"Twoja wizyta w {ctx.SalonName} ({ctx.ServiceName}, "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}) została {verb} przez salon.",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentCancelledBySalonEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
