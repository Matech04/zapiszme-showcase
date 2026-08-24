namespace App.Domain.Aggregates.NotificationAggregate;

/// <summary>
/// Kanał wychodzący, którego zużycie liczymy w panelu (in-app jest darmowy, więc go nie logujemy).
/// Odrębny od <c>App.Application.Notifications.NotificationChannelKind</c> — domena nie zależy od warstwy aplikacji.
/// </summary>
public enum NotificationDeliveryChannel
{
  Email = 1,
  Sms = 2,
}
