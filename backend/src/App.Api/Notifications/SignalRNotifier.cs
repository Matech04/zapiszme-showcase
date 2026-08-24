using App.Api.Hubs;
using App.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace App.Api.Notifications;

/// <summary>
/// Implementacja <see cref="IRealtimeNotifier"/> oparta o SignalR — pushuje powiadomienie do
/// grupy osobistej odbiorcy oraz do grupy „Recepcja" salonu. Metoda klienta:
/// <c>ReceiveNotification</c>.
/// </summary>
public sealed class SignalRNotifier : IRealtimeNotifier
{
  private readonly IHubContext<NotificationHub> _hub;

  public SignalRNotifier(IHubContext<NotificationHub> hub) => _hub = hub;

  public Task NotifyRecipientAsync(
    Guid tenantId,
    Guid? recipientUserId,
    RealtimeNotificationDto notification,
    CancellationToken ct)
  {
    // Konto kiosku nie jest bookowalnym pracownikiem, więc nigdy nie jest odbiorcą wizyty —
    // te dwie grupy się nie przecinają i nie ma ryzyka podwójnej dostawy.
    var groups = new List<string> { NotificationHub.DeskGroupName(tenantId) };
    if (recipientUserId is Guid userId)
    {
      groups.Add(NotificationHub.UserGroupName(userId));
    }

    return _hub.Clients
      .Groups(groups)
      .SendAsync("ReceiveNotification", notification, ct);
  }
}
