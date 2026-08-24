using App.Application.Common.Email;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Wysyła e-mail do moderatora platformy, gdy salon zgłosi custom szablon SMS do akceptacji.
/// Adres: <c>SmsTemplateModeration:NotifyEmail</c> (domyślnie <c>kontakt@zapisz.me</c>). Nazwa salonu
/// i proponowana treść są user-controlled — escapuje je <see cref="EmailRenderer"/> (ochrona przed
/// injection w treści maila).
/// </summary>
public sealed class SmsTemplateModerationNotifier : ISmsTemplateModerationNotifier
{
  private const string DefaultRecipient = "kontakt@zapisz.me";

  private readonly IEmailTransport _email;
  private readonly string _recipient;
  private readonly string? _dashboardBaseUrl;

  public SmsTemplateModerationNotifier(IEmailTransport email, IConfiguration config)
  {
    _email = email;
    var configured = config["SmsTemplateModeration:NotifyEmail"];
    _recipient = string.IsNullOrWhiteSpace(configured) ? DefaultRecipient : configured.Trim();
    _dashboardBaseUrl = config["Dashboard:BaseUrl"];
  }

  public Task NotifySubmittedForApprovalAsync(
    string salonName,
    NotificationType type,
    string proposedBody,
    CancellationToken ct = default)
  {
    var salon = string.IsNullOrWhiteSpace(salonName) ? "Salon" : salonName.Trim();

    var document = new EmailDocument
    {
      Heading = "Szablon SMS do akceptacji",
      Preheader = $"{salon} przesłał custom szablon SMS",
      Paragraphs = [$"Salon {salon} przesłał do akceptacji custom szablon SMS."],
      Details =
      [
        new EmailDetail("Salon", salon),
        new EmailDetail("Typ", SmsTemplateLabels.For(type)),
      ],
      Block = proposedBody ?? string.Empty,
      Cta = string.IsNullOrWhiteSpace(_dashboardBaseUrl)
        ? null
        : new EmailCallToAction(
          "Otwórz moderację szablonów SMS",
          $"{_dashboardBaseUrl.TrimEnd('/')}/admin/system/sms-templates"),
    };

    return _email.SendAsync(
      _recipient,
      $"Szablon SMS do akceptacji — {salon}",
      EmailRenderer.Render(document, EmailBrand.Platform),
      ct);
  }
}
