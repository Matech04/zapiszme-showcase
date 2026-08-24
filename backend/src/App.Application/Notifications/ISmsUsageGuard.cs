namespace App.Application.Notifications;

/// <summary>
/// Anti-abuse kill-switch: czy salon mieści się jeszcze w miesięcznym twardym limicie SMS.
/// Po osiągnięciu limitu blokujemy wszystkie wysyłki SMS (powiadomienia ORAZ OTP rezerwacji).
/// </summary>
public interface ISmsUsageGuard
{
  /// <summary>
  /// <c>true</c> jeśli liczba udanych SMS w bieżącym miesiącu (UTC) jest mniejsza niż
  /// <c>Subscription.EffectiveMonthlySmsCap</c>. Brak tenanta → <c>true</c> (nie blokujemy
  /// ścieżek bez kontekstu salonu, np. OTP rejestracji właściciela).
  /// </summary>
  Task<bool> IsWithinMonthlyCapAsync(Guid tenantId, CancellationToken ct);
}
