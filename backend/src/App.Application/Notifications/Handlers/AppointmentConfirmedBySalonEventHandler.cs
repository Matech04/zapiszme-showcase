using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Salon ręcznie potwierdził wizytę (Pending → Booked) — wysyła klientowi potwierdzenie
/// rezerwacji (ten sam typ co przy potwierdzeniu automatycznym).
/// </summary>
internal sealed class AppointmentConfirmedBySalonEventHandler
  : INotificationHandler<AppointmentConfirmedBySalonEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentConfirmedBySalonEventHandler> _logger;

  public AppointmentConfirmedBySalonEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentConfirmedBySalonEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentConfirmedBySalonEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      var recipient = ctx.CustomerRecipient();
      if (!ctx.Tenant.NotificationSettings.IsEnabled(NotificationType.BookingConfirmationToCustomer)
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

      await _dispatcher.DispatchAsync(new NotificationMessage(
        ctx.Tenant.Id,
        NotificationType.BookingConfirmationToCustomer,
        recipient,
        $"Rezerwacja potwierdzona — {ctx.SalonName}",
        $"Twoja rezerwacja w {ctx.SalonName} została potwierdzona: {ctx.ServiceName}, "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentConfirmedBySalonEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
