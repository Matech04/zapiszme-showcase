namespace App.Application.Notifications;

/// <summary>
/// Zbiorczy wynik rozesłania jednej wiadomości — po jednym wpisie na każdy kanał, który dispatcher
/// wziął pod uwagę. Kanały niezarejestrowane w DI (np. SMS bez tokenu) nie mają wpisu.
/// </summary>
public sealed record NotificationDispatchResult(IReadOnlyList<NotificationChannelOutcome> Outcomes)
{
  /// <summary>
  /// Czy wiadomość dotarła wskazanym kanałem. <see cref="NotificationDeliveryStatus.Suppressed"/>
  /// (demo-tenant) liczymy jako sukces — nic nie poszło w świat, ale to świadoma decyzja systemu,
  /// nie awaria. Brak wpisu dla kanału = kanał wyłączony = nie dostarczono.
  /// </summary>
  public bool Delivered(NotificationChannelKind kind) =>
    Outcomes.FirstOrDefault(o => o.Kind == kind) is
    {
      Status: NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.Suppressed,
    };
}

/// <summary>
/// Rozsyła <see cref="NotificationMessage"/> do wszystkich zarejestrowanych <see cref="INotificationChannel"/>.
/// Best-effort — awaria jednego kanału nie blokuje pozostałych. Wołający, dla których wysyłka jest
/// istotą operacji (jawna akcja personelu, np. „wyślij link do zadatku"), muszą sprawdzić
/// <see cref="NotificationDispatchResult.Delivered"/>; powiadomienia tła mogą wynik zignorować.
/// </summary>
public interface INotificationDispatcher
{
  Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct);
}
