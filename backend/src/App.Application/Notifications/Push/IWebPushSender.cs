namespace App.Application.Notifications.Push;

/// <summary>Wynik pojedynczej wysyłki Web Push.</summary>
public enum WebPushSendResult
{
  /// <summary>Push-service przyjął powiadomienie.</summary>
  Delivered = 1,

  /// <summary>Subskrypcja wygasła / została cofnięta (404/410) — należy skasować z bazy.</summary>
  Expired = 2,

  /// <summary>Inny błąd wysyłki (best-effort — pomijamy, nie kasujemy subskrypcji).</summary>
  Failed = 3,
}

/// <summary>
/// Wysyła zaszyfrowany payload Web Push na pojedynczą subskrypcję przeglądarki (protokół VAPID).
/// Best-effort — <c>NotificationDispatcher</c> izoluje wyjątki kanału.
/// </summary>
public interface IWebPushSender
{
  Task<WebPushSendResult> SendAsync(
    string endpoint,
    string p256dh,
    string auth,
    string payloadJson,
    CancellationToken ct);
}
