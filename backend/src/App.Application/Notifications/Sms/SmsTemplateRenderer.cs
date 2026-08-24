using System.Text.RegularExpressions;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Interpoluje custom treść szablonu SMS placeholderami z <see cref="NotificationPayload"/>.
/// Pola zmienne są przycinane tak samo jak w hardcoded <c>SmsTextBuilder</c> (salon 24 / klient 20 /
/// usługa 28 / kwota 10), żeby koszt pozostał przewidywalny. Nieznane/niedozwolone placeholdery są
/// usuwane (zastąpione pustym), więc literówka salonu nie wstrzykuje surowego tokenu do SMS-a.
///
/// UWAGA: renderer NIE robi finalnej sanityzacji ASCII/limitu — to robi wołający przez
/// <c>SmsTextBuilder.Sanitize</c> (Infrastructure), żeby ta sama logika kosztowa objęła obie ścieżki.
/// </summary>
public static class SmsTemplateRenderer
{
  private const int SalonMax = 24;
  private const int CustomerMax = 20;
  private const int ServiceMax = 28;
  private const int AmountMax = 10;

  private static readonly Regex PlaceholderPattern =
    new(@"\{([a-zA-Z]+)\}", RegexOptions.Compiled);

  public static string Render(string template, NotificationPayload p)
  {
    var date = p.Date?.ToString("dd.MM") ?? string.Empty;
    var time = p.StartTime?.ToString("HH:mm") ?? string.Empty;
    var oldDate = p.OldDate?.ToString("dd.MM") ?? string.Empty;
    var oldTime = p.OldStartTime?.ToString("HH:mm") ?? string.Empty;

    return PlaceholderPattern.Replace(template ?? string.Empty, match =>
    {
      var key = match.Groups[1].Value;
      return key switch
      {
        SmsTemplatePlaceholders.Salon => Clip(p.SalonName, SalonMax),
        SmsTemplatePlaceholders.Klient => Clip(p.CustomerName, CustomerMax),
        SmsTemplatePlaceholders.Usluga => Clip(p.ServiceName, ServiceMax),
        SmsTemplatePlaceholders.Data => date,
        SmsTemplatePlaceholders.Godzina => time,
        SmsTemplatePlaceholders.Pracownik => Clip(p.StaffName, CustomerMax),
        SmsTemplatePlaceholders.StaraData => oldDate,
        SmsTemplatePlaceholders.StaraGodzina => oldTime,
        SmsTemplatePlaceholders.Kwota => Clip(p.AmountText, AmountMax),
        SmsTemplatePlaceholders.Link => p.ActionUrl ?? string.Empty,
        // Nieznany token → usuwamy, żeby nie wyciekł surowy {cos} do treści.
        _ => string.Empty,
      };
    });
  }

  private static string Clip(string? value, int max)
  {
    var v = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    return v.Length <= max ? v : v[..max].TrimEnd();
  }
}
