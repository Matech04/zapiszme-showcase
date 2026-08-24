using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Zwraca w pełni wyrenderowaną (placeholdery podstawione + sanityzacja kosztowa) treść
/// ZATWIERDZONEGO custom szablonu SMS dla (tenant, typ) — albo <c>null</c>, gdy salon nie ma
/// zatwierdzonego szablonu dla tego typu. <c>null</c> oznacza fallback do hardcoded SmsTextBuilder.
/// </summary>
public interface ISmsTemplateResolver
{
  Task<string?> TryResolveAsync(
    Guid tenantId,
    NotificationType type,
    NotificationPayload payload,
    CancellationToken ct);
}
