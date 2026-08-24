using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications;

/// <summary>
/// Loguje każdą wychodzącą wysyłkę (SMS / e-mail) do dziennika zużycia, który zasila panel
/// „Zużycie SMS / e-mail". Best-effort — implementacja wycisza własne błędy, żeby zapis statystyki
/// nigdy nie wywrócił faktycznej wysyłki powiadomienia.
/// </summary>
public interface INotificationUsageRecorder
{
  Task RecordSmsAsync(Guid tenantId, NotificationType type, bool success, decimal points, CancellationToken ct);

  Task RecordEmailAsync(Guid tenantId, NotificationType type, bool success, CancellationToken ct);
}
