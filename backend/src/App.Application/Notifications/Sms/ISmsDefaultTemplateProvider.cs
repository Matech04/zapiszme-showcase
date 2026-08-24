using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Zwraca przykładowy podgląd DOMYŚLNEJ (hardcoded) treści SMS dla danego typu — pokazywany salonowi
/// jako wzorzec, gdy nie ma własnego szablonu. Implementacja w Infrastructure korzysta z
/// <c>SmsTextBuilder</c> z przykładowymi danymi (App.Application nie zna hardcoded szablonów).
/// </summary>
public interface ISmsDefaultTemplateProvider
{
  string GetDefaultPreview(NotificationType type);

  /// <summary>
  /// Renderuje custom treść szablonu przykładowymi danymi + finalna sanityzacja kosztowa
  /// (ASCII/GSM-7 + limit) — dokładnie tak, jak realnie pójdzie SMS. Podgląd dla admina/salonu.
  /// </summary>
  string RenderPreview(NotificationType type, string body);
}
