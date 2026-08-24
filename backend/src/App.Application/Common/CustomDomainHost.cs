namespace App.Application.Common;

/// <summary>
/// Pomocnik white-label hostów. Konwencja: klienta serwujemy pod <c>rezerwacja.{domena}</c>
/// (SPA bookingu) i <c>api.{domena}</c> (API), więc bazową domenę uzyskujemy zdejmując pierwszy
/// label hosta. Działa dla dowolnego TLD (zawsze prependujemy dokładnie jeden label).
/// </summary>
public static class CustomDomainHost
{
  /// <summary>
  /// Zdejmuje pierwszy label hosta → bazowa domena (np. <c>rezerwacja.salon-przyklad.pl</c>
  /// → <c>salon-przyklad.pl</c>; <c>api.example.co.uk</c> → <c>example.co.uk</c>).
  /// Zwraca <c>null</c>, gdy host jest pusty/niepoprawny lub nie ma subdomeny (apex
  /// <c>domena.tld</c>). Wynik znormalizowany (trim + lowercase, bez portu).
  /// </summary>
  public static string? ToBaseDomain(string? host)
  {
    if (string.IsNullOrWhiteSpace(host))
    {
      return null;
    }

    var normalized = host.Trim().ToLowerInvariant();

    // Odetnij ewentualny port (host z nagłówka może mieć ":443").
    var colon = normalized.IndexOf(':');
    if (colon >= 0)
    {
      normalized = normalized[..colon];
    }

    var dot = normalized.IndexOf('.');
    if (dot <= 0)
    {
      return null;
    }

    var rest = normalized[(dot + 1)..];
    // Bazowa domena musi mieć min. 2 labele (domena.tld) — inaczej zdejmowaliśmy apex.
    return rest.Contains('.') ? rest : null;
  }
}
