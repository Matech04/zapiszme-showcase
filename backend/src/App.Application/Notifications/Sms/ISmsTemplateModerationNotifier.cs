using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Powiadamia moderatora platformy (e-mail, domyślnie kontakt@zapisz.me) o nowym custom szablonie SMS
/// zgłoszonym przez salon do akceptacji. Best-effort — nie może wywrócić zapisu szablonu. Adres
/// odbiorcy konfigurowalny przez <c>SmsTemplateModeration:NotifyEmail</c>.
/// </summary>
public interface ISmsTemplateModerationNotifier
{
  Task NotifySubmittedForApprovalAsync(
    string salonName,
    NotificationType type,
    string proposedBody,
    CancellationToken ct = default);
}
