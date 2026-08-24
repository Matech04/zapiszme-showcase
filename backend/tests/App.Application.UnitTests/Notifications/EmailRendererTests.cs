using App.Application.Common.Email;
using App.Infrastructure.Email;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// EMAIL-RENDER-* — wspólny layout maili: escapowanie, kontrast akcentu salonu,
/// pomijanie pustych wierszy i zgodność wersji tekstowej z HTML-em.
/// </summary>
public sealed class EmailRendererTests
{
  private static readonly EmailDocument Sample = new()
  {
    Heading = "Rezerwacja potwierdzona",
    Paragraphs = ["Witaj Jan, Twoja wizyta została potwierdzona."],
    Details =
    [
      new EmailDetail("Usługa", "Strzyżenie"),
      new EmailDetail("Pracownik", null),
      new EmailDetail("Data", "01.06.2026"),
    ],
    Cta = new EmailCallToAction("Zapłać zadatek", "https://zapisz.me/pay/abc"),
    Footnote = "Do zobaczenia!",
  };

  [Fact]
  public void Render_EscapesUserSuppliedText()
  {
    var document = Sample with
    {
      Heading = "<script>alert(1)</script>",
      Details = [new EmailDetail("Klient", "<img src=x onerror=alert(1)>")],
    };

    var body = EmailRenderer.Render(document, EmailBrand.Platform);

    Assert.DoesNotContain("<script>", body.Html, StringComparison.Ordinal);
    Assert.DoesNotContain("<img src=x", body.Html, StringComparison.Ordinal);
    Assert.Contains("&lt;script&gt;", body.Html, StringComparison.Ordinal);
  }

  [Fact]
  public void Render_EscapesBrandNameFromTenant()
  {
    var brand = EmailBrand.ForTenant("<b>Salon</b>", accentHex: null);

    var body = EmailRenderer.Render(Sample, brand);

    Assert.DoesNotContain("<b>Salon</b>", body.Html, StringComparison.Ordinal);
    Assert.Contains("&lt;b&gt;Salon&lt;/b&gt;", body.Html, StringComparison.Ordinal);
  }

  [Fact]
  public void Render_OmitsDetailRowsWithoutValue()
  {
    var body = EmailRenderer.Render(Sample, EmailBrand.Platform);

    Assert.Contains("Usługa", body.Html, StringComparison.Ordinal);
    Assert.DoesNotContain("Pracownik", body.Html, StringComparison.Ordinal);
    Assert.DoesNotContain("Pracownik", body.Text, StringComparison.Ordinal);
  }

  [Fact]
  public void Render_TextVersion_CarriesContentWithoutMarkup()
  {
    var body = EmailRenderer.Render(Sample, EmailBrand.Platform);

    Assert.DoesNotContain('<', body.Text);
    Assert.Contains("Rezerwacja potwierdzona", body.Text, StringComparison.Ordinal);
    Assert.Contains("Usługa: Strzyżenie", body.Text, StringComparison.Ordinal);
    Assert.Contains("https://zapisz.me/pay/abc", body.Text, StringComparison.Ordinal);
  }

  [Fact]
  public void Render_UsesTenantAccentForHeaderAndButton()
  {
    var brand = EmailBrand.ForTenant("Studio Ani", "#0EA5E9");

    var body = EmailRenderer.Render(Sample, brand);

    Assert.Contains("background:#0EA5E9", body.Html, StringComparison.Ordinal);
    Assert.Contains("Studio Ani", body.Html, StringComparison.Ordinal);
  }

  /// <summary>
  /// „zapisz.me” wygląda dla Gmaila jak domena — bez własnej kotwicy klient pocztowy autolinkuje
  /// tekst i nadaje mu styl linku (niebieski + podkreślenie), przez co nazwa ginęła na fioletowym
  /// pasku. Nagłówek MUSI więc być własnym &lt;a&gt; z jawnym kolorem i bez podkreślenia.
  /// </summary>
  [Fact]
  public void Render_PlatformBrandName_IsOwnAnchorWithoutUnderline()
  {
    var body = EmailRenderer.Render(Sample, EmailBrand.Platform);

    Assert.Contains("<a href=\"https://zapisz.me\"", body.Html, StringComparison.Ordinal);
    Assert.Contains("text-decoration:none", body.Html, StringComparison.Ordinal);
    // Kolor tekstu na akcencie, nie domyślny niebieski klienta pocztowego.
    Assert.Contains("color:#ffffff", body.Html, StringComparison.Ordinal);
    // Nazwa nie może zostać w gołym <span>, bo wtedy Gmail ją przejmie.
    Assert.DoesNotContain("""<span style="font-size:24px""", body.Html, StringComparison.Ordinal);
  }

  /// <summary>Nazwa salonu nie jest domeną — nie podpinamy jej pod adres platformy.</summary>
  [Fact]
  public void Render_TenantBrandName_IsNotLinked()
  {
    var brand = EmailBrand.ForTenant("Studio Ani", "#0EA5E9");

    var body = EmailRenderer.Render(Sample, brand);

    Assert.Null(brand.SiteUrl);
    Assert.Contains("""<span style="font-size:24px""", body.Html, StringComparison.Ordinal);
  }

  /// <summary>Salon bez nazwy spada na markę platformy — i musi dostać ten sam anti-autolink.</summary>
  [Fact]
  public void Render_TenantWithoutName_FallsBackToLinkedPlatformName()
  {
    var brand = EmailBrand.ForTenant(name: null, accentHex: null);

    Assert.Equal("zapisz.me", brand.Name);
    Assert.Equal("https://zapisz.me", brand.SiteUrl);
  }

  /// <summary>Stopka też wymieniała „zapisz.me” gołym tekstem — Gmail linkował ją na niebiesko.</summary>
  [Fact]
  public void Render_FooterBrandMention_IsAnchoredAndMuted()
  {
    var body = EmailRenderer.Render(Sample, EmailBrand.Platform);

    Assert.Contains("przez <a href=\"https://zapisz.me\"", body.Html, StringComparison.Ordinal);
    // Wersja tekstowa zostaje bez znaczników.
    Assert.Contains("przez zapisz.me", body.Text, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("czerwony")]
  [InlineData("#GGGGGG")]
  [InlineData("#fff")]
  public void ForTenant_InvalidAccent_FallsBackToPlatformColor(string? accentHex)
  {
    var brand = EmailBrand.ForTenant("Studio Ani", accentHex);

    Assert.Equal(EmailBrand.Platform.AccentHex, brand.AccentHex);
  }

  [Fact]
  public void ForTenant_BlankName_FallsBackToPlatformName()
  {
    Assert.Equal(EmailBrand.Platform.Name, EmailBrand.ForTenant("   ", "#0EA5E9").Name);
  }

  // Salon może ustawić pastelowy akcent — biały tekst byłby na nim nieczytelny.
  [Theory]
  [InlineData("#FDE68A", "#111827")]
  [InlineData("#FFFFFF", "#111827")]
  [InlineData("#7C3AED", "#ffffff")]
  [InlineData("#000000", "#ffffff")]
  public void AccentTextHex_SwitchesWithAccentLuminance(string accentHex, string expectedTextHex)
  {
    Assert.Equal(expectedTextHex, new EmailBrand("Salon", accentHex).AccentTextHex);
  }
}
