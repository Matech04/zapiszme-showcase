namespace App.Application.Notifications;

/// <summary>Kanał dostarczania powiadomień.</summary>
public enum NotificationChannelKind
{
  Email = 1,
  InApp = 2,
  Sms = 3,
  WebPush = 4,
}

/// <summary>
/// Wynik próby dostarczenia powiadomienia jednym kanałem. Rozróżnia „nie wysłano, bo nie było po co"
/// (<see cref="Skipped"/>) od „nie wysłano, bo się nie udało" (<see cref="Failed"/>) — bez tego
/// rozróżnienia handler jawnej akcji personelu nie wie, czy wolno zaraportować sukces.
/// </summary>
public enum NotificationDeliveryStatus
{
  /// <summary>Kanał dostarczył wiadomość.</summary>
  Sent = 1,

  /// <summary>Kanał świadomie nic nie zrobił — odbiorca bez adresu / telefonu / konta w panelu.</summary>
  Skipped = 2,

  /// <summary>Wysyłkę wyciszył dispatcher (demo-tenant). Dla wołającego to sukces.</summary>
  Suppressed = 3,

  /// <summary>Kanał rzucił wyjątkiem (API dostawcy, SMTP, DB).</summary>
  Failed = 4,

  /// <summary>Kanał przekroczył twardy timeout dispatchera.</summary>
  TimedOut = 5,
}

/// <summary>Co się stało z jednym kanałem w ramach jednego <see cref="NotificationMessage"/>.</summary>
/// <param name="Kind">Kanał.</param>
/// <param name="Status">Wynik.</param>
/// <param name="Reason">Krótki powód, bezpieczny do zalogowania (bez PII). Null dla <see cref="NotificationDeliveryStatus.Sent"/>.</param>
public sealed record NotificationChannelOutcome(
  NotificationChannelKind Kind,
  NotificationDeliveryStatus Status,
  string? Reason = null);

/// <summary>
/// Pojedynczy kanał dostarczania powiadomień (e-mail / in-app / SMS). Implementacje są best-effort
/// — <see cref="NotificationDispatcher"/> izoluje błędy każdego kanału osobno. Kanał zwraca
/// <see cref="NotificationDeliveryStatus.Sent"/> albo <see cref="NotificationDeliveryStatus.Skipped"/>;
/// pozostałe statusy nadaje dispatcher na podstawie wyjątku lub timeoutu.
/// </summary>
public interface INotificationChannel
{
  NotificationChannelKind Kind { get; }

  Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct);
}
