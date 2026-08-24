using App.Application.Payments;

namespace App.Application.UnitTests.Payments;

/// <summary>
/// Bazy URL-i linku zadatku: white-label (CustomDomain) → rezerwacja.&lt;domena&gt; dla obu (/p i /platnosc),
/// zwykły salon → globalny config (bez zmian).
/// </summary>
public class DepositUrlsTests
{
  [Fact]
  public void Resolve_WithCustomDomain_UsesRezerwacjaHostForBoth()
  {
    var (shortBase, webBase) = DepositUrls.Resolve(
      customDomain: "salon-przyklad.pl",
      configWebBaseUrl: "https://zapisz.me",
      configShortBaseUrl: "https://zapisz.me");

    Assert.Equal("https://rezerwacja.salon-przyklad.pl", shortBase);
    Assert.Equal("https://rezerwacja.salon-przyklad.pl", webBase);
  }

  [Fact]
  public void Resolve_WithCustomDomain_NormalizesCasingAndWhitespace()
  {
    var (shortBase, webBase) = DepositUrls.Resolve(
      customDomain: "  Salon- Nowak.PL ".Replace(" ", ""), // symulacja wejścia
      configWebBaseUrl: "https://zapisz.me",
      configShortBaseUrl: null);

    Assert.Equal("https://rezerwacja.salon-nowak.pl", shortBase);
    Assert.Equal("https://rezerwacja.salon-nowak.pl", webBase);
  }

  [Fact]
  public void Resolve_WithoutCustomDomain_UsesConfigAndTrimsTrailingSlash()
  {
    var (shortBase, webBase) = DepositUrls.Resolve(
      customDomain: null,
      configWebBaseUrl: "https://zapisz.me/",
      configShortBaseUrl: "https://zapisz.me/");

    Assert.Equal("https://zapisz.me", shortBase);
    Assert.Equal("https://zapisz.me", webBase);
  }

  [Fact]
  public void Resolve_WithoutShortConfig_FallsBackToWebBase()
  {
    var (shortBase, webBase) = DepositUrls.Resolve(
      customDomain: "   ", // puste/whitespace = brak white-label
      configWebBaseUrl: "https://zapisz.me",
      configShortBaseUrl: null);

    Assert.Equal("https://zapisz.me", shortBase);
    Assert.Equal("https://zapisz.me", webBase);
  }
}
