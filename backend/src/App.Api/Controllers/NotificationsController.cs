using App.Application.Notifications.Commands.MarkAllNotificationsRead;
using App.Application.Notifications.Commands.MarkNotificationRead;
using App.Application.Notifications.Push.Commands.SubscribePush;
using App.Application.Notifications.Push.Commands.UnsubscribePush;
using App.Application.Notifications.Push.Queries.GetVapidPublicKey;
using App.Application.Notifications.Queries.GetCustomerChangedAppointments;
using App.Application.Notifications.Queries.GetNotifications;
using App.Application.Notifications.Queries.GetNotificationUsage;
using App.Application.Notifications.Queries.GetOutsideScheduleAppointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")]
public class NotificationsController : ApiControllerBase
{
  /// <summary>
  /// Wizyty w zadanym zakresie dat, które nie mieszczą się w aktualnym grafiku /
  /// override’ach / urlopach. Endpoint zasila centrum powiadomień (dzwonek w panelu).
  /// </summary>
  [HttpGet("outside-schedule")]
  public async Task<ActionResult<List<OutsideScheduleNotificationDto>>> GetOutsideSchedule(
      [FromQuery] DateOnly from,
      [FromQuery] DateOnly to)
  {
    var result = await Mediator.Send(new GetOutsideScheduleAppointmentsQuery(from, to));
    return Ok(result);
  }

  /// <summary>
  /// Wizyty anulowane lub przełożone przez klienta (samoobsługa) w ciągu ostatnich 48h.
  /// Endpoint zasila centrum powiadomień (dzwonek w panelu).
  /// </summary>
  [HttpGet("customer-changes")]
  public async Task<ActionResult<List<CustomerChangedAppointmentDto>>> GetCustomerChanges()
  {
    var result = await Mediator.Send(new GetCustomerChangedAppointmentsQuery());
    return Ok(result);
  }

  /// <summary>
  /// Trwałe powiadomienia in-app bieżącego salonu (zdarzeniowe — rezerwacje, anulowania,
  /// przełożenia). Zasila dzwonek; uzupełniane na bieżąco pushem SignalR.
  /// </summary>
  [HttpGet]
  public async Task<ActionResult<List<NotificationDto>>> GetNotifications()
  {
    var result = await Mediator.Send(new GetNotificationsQuery());
    return Ok(result);
  }

  /// <summary>
  /// Zużycie kanałów wychodzących (SMS / e-mail) salonu w danym miesiącu — liczba wysyłek,
  /// punkty SMS smsapi.pl, limit z planu i nadwyżka. Zasila panel „Zużycie SMS / e-mail".
  /// Domyślnie bieżący miesiąc (UTC). Tylko właściciel/manager.
  /// </summary>
  [HttpGet("usage")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult<NotificationUsageDto>> GetUsage(
      [FromQuery] int? year,
      [FromQuery] int? month)
  {
    var result = await Mediator.Send(new GetNotificationUsageQuery(year, month));
    return Ok(result);
  }

  /// <summary>Oznacza wszystkie powiadomienia salonu jako przeczytane.</summary>
  [HttpPost("read-all")]
  public async Task<ActionResult> MarkAllRead()
  {
    await Mediator.Send(new MarkAllNotificationsReadCommand());
    return NoContent();
  }

  /// <summary>Oznacza pojedyncze powiadomienie jako przeczytane.</summary>
  [HttpPost("{id:guid}/read")]
  public async Task<ActionResult> MarkRead(Guid id)
  {
    await Mediator.Send(new MarkNotificationReadCommand(id));
    return NoContent();
  }

  /// <summary>
  /// Publiczny klucz VAPID do subskrypcji Web Push w przeglądarce (+ flaga czy push jest włączony).
  /// Klucz publiczny nie jest sekretem — służy do zaszyfrowania subskrypcji po stronie klienta.
  /// </summary>
  [HttpGet("push/vapid-public-key")]
  public async Task<ActionResult<VapidPublicKeyDto>> GetVapidPublicKey()
  {
    var result = await Mediator.Send(new GetVapidPublicKeyQuery());
    return Ok(result);
  }

  /// <summary>Rejestruje subskrypcję Web Push bieżącego urządzenia (użytkownik włączył powiadomienia).</summary>
  [HttpPost("push/subscribe")]
  public async Task<ActionResult> SubscribePush([FromBody] SubscribePushRequest request)
  {
    await Mediator.Send(new SubscribePushCommand(request.Endpoint, request.P256dh, request.Auth));
    return NoContent();
  }

  /// <summary>Usuwa subskrypcję Web Push (użytkownik wyłączył powiadomienia na urządzeniu).</summary>
  [HttpPost("push/unsubscribe")]
  public async Task<ActionResult> UnsubscribePush([FromBody] UnsubscribePushRequest request)
  {
    await Mediator.Send(new UnsubscribePushCommand(request.Endpoint));
    return NoContent();
  }
}

/// <summary>Payload subskrypcji Web Push z <c>PushSubscription.toJSON()</c> przeglądarki.</summary>
public record SubscribePushRequest(string Endpoint, string P256dh, string Auth);

public record UnsubscribePushRequest(string Endpoint);
