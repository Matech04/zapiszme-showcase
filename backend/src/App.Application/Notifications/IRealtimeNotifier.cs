namespace App.Application.Notifications;

/// <summary>
/// Push powiadomień in-app w czasie rzeczywistym do podłączonych klientów dashboardu salonu.
/// Abstrakcja w App.Application — implementacja (SignalR) żyje w App.Api.
/// </summary>
public interface IRealtimeNotifier
{
  /// <summary>
  /// Wysyła powiadomienie do konkretnego odbiorcy (jego otwarte karty panelu) oraz do konta
  /// „Recepcja" tego salonu, które obsługuje wizyty całego zespołu. Nigdy nie broadcastuje do
  /// całego tenanta — dzwonek jest osobisty.
  /// </summary>
  Task NotifyRecipientAsync(
    Guid tenantId,
    Guid? recipientUserId,
    RealtimeNotificationDto notification,
    CancellationToken ct);
}
