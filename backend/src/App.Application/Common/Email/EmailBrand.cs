using System.Globalization;
using System.Text.RegularExpressions;

namespace App.Application.Common.Email;

/// <summary>
/// Wygląd marki w mailu: nazwa na pasku nagłówka i kolor akcentu (pasek + przycisk CTA).
/// Maile do klientów salonu noszą markę salonu (<see cref="ForTenant"/>); maile kontowe
/// (reset hasła, potwierdzenie adresu) i feedback — markę <see cref="Platform"/>, bo prowadzą
/// do centralnego dashboardu, nie do salonu.
/// </summary>
/// <param name="SiteUrl">
/// Adres, pod który podpina się nazwa w pasku nagłówka. Dla marki platformy MUSI być ustawiony:
/// „zapisz.me” wygląda dla Gmaila jak domena, więc bez własnego <c>&lt;a&gt;</c> klient pocztowy
/// autolinkuje ten tekst i narzuca mu własny styl (niebieski, podkreślony) — na fioletowym pasku
/// nazwa robiła się wtedy praktycznie niewidoczna. Dla salonu <c>null</c>: nazwa nie jest domeną,
/// a jego adresu tu nie znamy.
/// </param>
public sealed partial record EmailBrand(string Name, string AccentHex, string? SiteUrl = null)
{
  private const string DefaultAccentHex = "#7C3AED";

  public static EmailBrand Platform { get; } = new("zapisz.me", DefaultAccentHex, "https://zapisz.me");

  /// <summary>
  /// Marka salonu. <paramref name="accentHex"/> to <c>Tenant.BookingCalendarColorHex</c> — gdy
  /// pusty albo nie jest poprawnym <c>#RRGGBB</c>, spadamy na akcent domyślny.
  /// </summary>
  public static EmailBrand ForTenant(string? name, string? accentHex) => new(
    string.IsNullOrWhiteSpace(name) ? Platform.Name : name.Trim(),
    accentHex is not null && HexColor().IsMatch(accentHex) ? accentHex : DefaultAccentHex,
    // Gdy salon nie podał nazwy, spadamy na „zapisz.me” — a wtedy potrzebny jest ten sam
    // anti-autolink co w marce platformy.
    string.IsNullOrWhiteSpace(name) ? Platform.SiteUrl : null);

  /// <summary>
  /// Kolor tekstu kładzionego na akcencie. Salon może ustawić jasny akcent (np. pastelowy żółty),
  /// na którym biały tekst jest nieczytelny — wtedy przełączamy na ciemny.
  /// </summary>
  public string AccentTextHex => RelativeLuminance(AccentHex) > 0.5 ? "#111827" : "#ffffff";

  // Luminancja względna wg WCAG 2.x — ten sam wzór, którego używa kontrast tekstu w przeglądarce.
  private static double RelativeLuminance(string hex)
  {
    var r = Channel(hex.Substring(1, 2));
    var g = Channel(hex.Substring(3, 2));
    var b = Channel(hex.Substring(5, 2));
    return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
  }

  private static double Channel(string component)
  {
    var value = int.Parse(component, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
  }

  [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
  private static partial Regex HexColor();
}
