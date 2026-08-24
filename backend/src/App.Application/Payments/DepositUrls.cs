namespace App.Application.Payments;

/// <summary>
/// Wyznacza bazę URL-i linku zadatku. Dla salonu white-label (<c>Tenant.CustomDomain</c> ustawione)
/// zarówno krótki link <c>/p/{code}</c>, jak i powrót po zapłacie <c>/platnosc/*</c> lądują na
/// <c>rezerwacja.{domena}</c> — spójnie z resztą bookingu (konwencja hostów). Bez custom domeny
/// używamy globalnego configu (zachowanie bez zmian dla zwykłych salonów).
/// </summary>
public static class DepositUrls
{
  /// <returns>
  /// <c>ShortBase</c> — baza dla <c>/p/{code}</c>; <c>WebBase</c> — baza dla <c>/platnosc/*</c>.
  /// Oba bez końcowego ukośnika.
  /// </returns>
  public static (string ShortBase, string WebBase) Resolve(
      string? customDomain,
      string? configWebBaseUrl,
      string? configShortBaseUrl)
  {
    if (!string.IsNullOrWhiteSpace(customDomain))
    {
      var host = $"https://rezerwacja.{customDomain.Trim().ToLowerInvariant()}";
      return (host, host);
    }

    var web = (configWebBaseUrl ?? string.Empty).TrimEnd('/');
    var shortBase = (configShortBaseUrl ?? configWebBaseUrl ?? string.Empty).TrimEnd('/');
    return (shortBase, web);
  }
}
