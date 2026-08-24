using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Pracownik ręcznie wystawił wizytę w panelu — wysyła klientowi potwierdzenie rezerwacji
/// kanałem komunikacji z klientem, zależnie od <see cref="NotificationSettings"/> tenanta.
/// Gdy klient nie ma rekordu/kontaktu (np. wizyta na gościa bez danych), powiadomienie pomijamy.
/// </summary>
internal sealed class StaffBookedAppointmentEventHandler
  : INotificationHandler<StaffBookedAppointmentEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<StaffBookedAppointmentEventHandler> _logger;

  public StaffBookedAppointmentEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<StaffBookedAppointmentEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(StaffBookedAppointmentEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      var recipient = ctx.CustomerRecipient();
      if (!ctx.Tenant.NotificationSettings.IsEnabled(NotificationType.StaffBookedAppointmentToCustomer)
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
        NotificationType.StaffBookedAppointmentToCustomer,
        recipient,
        $"Rezerwacja w {ctx.SalonName}",
        $"{ctx.SalonName} zarezerwował dla Ciebie wizytę: {ctx.ServiceName}, "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć StaffBookedAppointmentEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
