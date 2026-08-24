using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Renderuje DOMYŚLNĄ (hardcoded) treść SMS z przykładowymi danymi jako podgląd dla salonu.
/// Korzysta z <see cref="SmsTextBuilder"/> — jedyne źródło prawdy o hardcoded szablonach.
/// </summary>
public sealed class SmsDefaultTemplateProvider : ISmsDefaultTemplateProvider
{
  private static readonly NotificationPayload SamplePayload = new(
    SalonName: "Twoj Salon",
    CustomerName: "Anna",
    StaffName: "Kasia",
    ServiceName: "Manicure",
    Date: new DateOnly(2026, 6, 25),
    StartTime: new TimeOnly(14, 30),
    OldDate: new DateOnly(2026, 6, 20),
    OldStartTime: new TimeOnly(10, 0),
    ActionUrl: "https://zps.me/x",
    AmountText: "50 PLN");

  public string GetDefaultPreview(NotificationType type) =>
    SmsTextBuilder.Build(type, SamplePayload);

  public string RenderPreview(NotificationType type, string body) =>
    SmsTextBuilder.SanitizeUnicode(SmsTemplateRenderer.Render(body, SamplePayload));
}
