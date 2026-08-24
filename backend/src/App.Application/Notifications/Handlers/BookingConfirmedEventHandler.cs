using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Po potwierdzeniu rezerwacji publicznej wysyła powiadomienie do salonu (pracownika)
/// i potwierdzenie do klienta — każde zależne od <see cref="NotificationSettings"/> tenanta.
/// </summary>
internal sealed class BookingConfirmedEventHandler : INotificationHandler<BookingConfirmedEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<BookingConfirmedEventHandler> _logger;

  public BookingConfirmedEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<BookingConfirmedEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(BookingConfirmedEvent e, CancellationToken ct)
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
        CustomerFullName: ctx.CustomerFullName,
        CustomerPhone: ctx.CustomerPhone,
        CustomerEmail: ctx.CustomerEmail);

      var settings = ctx.Tenant.NotificationSettings;

      if (settings.IsEnabled(NotificationType.NewBookingToSalon)
          && !string.IsNullOrWhiteSpace(ctx.Employee.Email))
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.NewBookingToSalon,
          new NotificationRecipient(ctx.Employee.Email, null, ctx.Employee.UserId, ctx.StaffName),
          $"Nowa rezerwacja online — {ctx.CustomerName}, {ctx.Appointment.Date:dd.MM.yyyy}",
          $"Nowa rezerwacja: {ctx.CustomerName}, {ctx.ServiceName}, "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }

      var customerRecipient = ctx.CustomerRecipient();
      if (settings.IsEnabled(NotificationType.BookingConfirmationToCustomer)
          && customerRecipient is not null)
      {
        await _dispatcher.DispatchAsync(new NotificationMessage(
          ctx.Tenant.Id,
          NotificationType.BookingConfirmationToCustomer,
          customerRecipient,
          $"Potwierdzenie rezerwacji — {ctx.SalonName}",
          $"Twoja rezerwacja w {ctx.SalonName} potwierdzona: {ctx.ServiceName}, "
            + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
          payload,
          ctx.Appointment.Id), ct);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć BookingConfirmedEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
