using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Po przełożeniu wizyty przez klienta wysyła powiadomienie do salonu (pracownika)
/// i potwierdzenie do klienta — każde zależne od <see cref="NotificationSettings"/> tenanta.
/// </summary>
internal sealed class AppointmentRescheduledEventHandler : INotificationHandler<AppointmentRescheduledEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<AppointmentRescheduledEventHandler> _logger;

  public AppointmentRescheduledEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<AppointmentRescheduledEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(AppointmentRescheduledEvent e, CancellationToken ct)
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
        StartTime: ctx.Appointment.StartTime,
        OldDate: e.OldDate,
        OldStartTime: e.OldStartTime);

      var settings = ctx.Tenant.NotificationSettings;

      if (settings.IsEnabled(NotificationType.RescheduleToSalon)
          && !string.IsNullOrWhiteSpace(ctx.Employee.Email))
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.RescheduleToSalon,
          new NotificationRecipient(ctx.Employee.Email, null, ctx.Employee.UserId, ctx.StaffName),
          $"Wizyta przełożona — {ctx.CustomerName}",
          $"Klient {ctx.CustomerName} przełożył wizytę ({ctx.ServiceName}) z "
            + $"{e.OldDate:dd.MM.yyyy} {e.OldStartTime:HH:mm} na "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }

      var customerRecipient = ctx.CustomerRecipient();
      if (settings.IsEnabled(NotificationType.RescheduleToCustomer)
          && customerRecipient is not null)
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.RescheduleToCustomer,
          customerRecipient,
          $"Wizyta przełożona — {ctx.SalonName}",
          $"Twoja wizyta w {ctx.SalonName} ({ctx.ServiceName}) została przełożona na "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć AppointmentRescheduledEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
