using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Tryb Manual: po OTP rezerwacja jest Pending. Powiadamia salon (pracownika), że wizyta
/// czeka na ręczne potwierdzenie — zależne od <see cref="NotificationSettings"/> tenanta.
/// </summary>
internal sealed class BookingAwaitingConfirmationEventHandler
  : INotificationHandler<BookingAwaitingConfirmationEvent>
{
  private readonly IApplicationDbContext _context;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ILogger<BookingAwaitingConfirmationEventHandler> _logger;

  public BookingAwaitingConfirmationEventHandler(
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ILogger<BookingAwaitingConfirmationEventHandler> logger)
  {
    _context = context;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task Handle(BookingAwaitingConfirmationEvent e, CancellationToken ct)
  {
    try
    {
      var ctx = await NotificationContextLoader.LoadAsync(_context, e.TenantId, e.AppointmentId, ct);
      if (ctx is null)
      {
        return;
      }

      if (!ctx.Tenant.NotificationSettings.IsEnabled(NotificationType.AwaitingConfirmationToSalon)
          || string.IsNullOrWhiteSpace(ctx.Employee.Email))
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

      await _dispatcher.DispatchAsync(new NotificationMessage(
        ctx.Tenant.Id,
        NotificationType.AwaitingConfirmationToSalon,
        new NotificationRecipient(ctx.Employee.Email, null, ctx.Employee.UserId, ctx.StaffName),
        $"Rezerwacja czeka na potwierdzenie — {ctx.CustomerName}, {ctx.Appointment.Date:dd.MM.yyyy}",
        $"Rezerwacja do potwierdzenia: {ctx.CustomerName}, {ctx.ServiceName}, "
          + $"{ctx.Appointment.Date:dd.MM.yyyy} {ctx.Appointment.StartTime:HH:mm}",
        payload,
        ctx.Appointment.Id), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Nie udało się obsłużyć BookingAwaitingConfirmationEvent dla wizyty {Id}", e.AppointmentId);
    }
  }
}
