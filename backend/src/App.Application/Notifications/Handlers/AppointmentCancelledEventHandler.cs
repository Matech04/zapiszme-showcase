using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Po anulowaniu wizyty przez klienta wysyła powiadomienie do salonu (pracownika)
/// i potwierdzenie do klienta — każde zależne od <see cref="NotificationSettings"/> tenanta.
/// </summary>
internal sealed class AppointmentCancelledEventHandler : INotificationHandler<AppointmentCancelledEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentCancelledEventHandler> _logger;

  public AppointmentCancelledEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentCancelledEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentCancelledEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
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

      var settings = ctx.Tenant.NotificationSettings;

      if (settings.IsEnabled(NotificationType.CancellationToSalon)
          && !string.IsNullOrWhiteSpace(ctx.Employee.Email))
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.CancellationToSalon,
          new NotificationRecipient(ctx.Employee.Email, null, ctx.Employee.UserId, ctx.StaffName),
          $"Wizyta anulowana — {ctx.CustomerName}, {ctx.Appointment.Date:dd.MM.yyyy}",
          $"Klient {ctx.CustomerName} anulował wizytę: {ctx.ServiceName}, "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }

      var customerRecipient = ctx.CustomerRecipient();
      if (settings.IsEnabled(NotificationType.CancellationToCustomer)
          && customerRecipient is not null)
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.CancellationToCustomer,
          customerRecipient,
          $"Wizyta anulowana — {ctx.SalonName}",
          $"Twoja wizyta w {ctx.SalonName} została anulowana: {ctx.ServiceName}, "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentCancelledEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
