using App.Application.Common;

namespace App.Application.UnitTests.Common;

/// <summary>
/// VAT-CAT-001 — IVatRateCatalog: pełny zestaw PL + zachowanie dla unknown country.
/// </summary>
public sealed class VatRateCatalogTests
{
  [Fact]
  public void PL_returns_full_set_of_polish_rates()
  {
    var catalog = new VatRateCatalog();

    var rates = catalog.GetForCountry("PL");

    Assert.Equal(5, rates.Count);
    Assert.Contains(rates, r => r.Name == "zw." && r.Value == 0.00m && r.IsDefault);
    Assert.Contains(rates, r => r.Name == "23%" && r.Value == 0.23m && !r.IsDefault);
    Assert.Contains(rates, r => r.Name == "8%" && r.Value == 0.08m && !r.IsDefault);
    Assert.Contains(rates, r => r.Name == "5%" && r.Value == 0.05m && !r.IsDefault);
    Assert.Contains(rates, r => r.Name == "0%" && r.Value == 0.00m && !r.IsDefault);
  }

  [Fact]
  public void PL_has_exactly_one_default_rate()
  {
    var catalog = new VatRateCatalog();

    var rates = catalog.GetForCountry("PL");

    Assert.Single(rates, r => r.IsDefault);
  }

  [Fact]
  public void Country_lookup_is_case_insensitive()
  {
    var catalog = new VatRateCatalog();

    Assert.NotEmpty(catalog.GetForCountry("pl"));
    Assert.True(catalog.SupportsCountry("pl"));
    Assert.True(catalog.SupportsCountry("PL"));
  }

  [Fact]
  public void Unknown_country_returns_empty()
  {
    var catalog = new VatRateCatalog();

    Assert.Empty(catalog.GetForCountry("XX"));
    Assert.False(catalog.SupportsCountry("XX"));
  }

  [Fact]
  public void All_PL_rate_values_satisfy_VatRate_invariants()
  {
    var catalog = new VatRateCatalog();
    var tenantId = Guid.NewGuid();

    foreach (var template in catalog.GetForCountry("PL"))
    {
      var vat = new VatRate(tenantId, template.Name, template.Value, template.IsDefault);
      Assert.Equal(template.Value, vat.Value);
      Assert.Equal(template.IsDefault, vat.IsDefault);
    }
  }
}
