namespace App.Application.Common;

public sealed record VatRateTemplate(string Name, decimal Value, bool IsDefault);

/// <summary>
/// Zwraca zestaw szablonów stawek VAT do zaseedowania dla nowego tenanta na podstawie kodu kraju
/// (ISO 3166-1 alpha-2). Future-proof: dziś tylko PL, w przyszłości dochodzą kolejne kraje bez
/// zmian w kodzie wywołującym — wystarczy poszerzyć słownik.
/// </summary>
public interface IVatRateCatalog
{
  IReadOnlyList<VatRateTemplate> GetForCountry(string countryCode);
  bool SupportsCountry(string countryCode);
}

public sealed class VatRateCatalog : IVatRateCatalog
{
  private static readonly IReadOnlyDictionary<string, IReadOnlyList<VatRateTemplate>> Catalogs =
    new Dictionary<string, IReadOnlyList<VatRateTemplate>>(StringComparer.OrdinalIgnoreCase)
    {
      ["PL"] = new VatRateTemplate[]
      {
        // „zw." (zwolnienie podmiotowe art. 113 ustawy o VAT) jest domyślną stawką
        // dla nowych usług — większość solo-tenantów w branży beauty korzysta ze
        // zwolnienia. „23%" zostaje w katalogu, ale bez flagi IsDefault.
        new("zw.", 0.00m, IsDefault: true),
        new("23%", 0.23m, IsDefault: false),
        new("8%", 0.08m, IsDefault: false),
        new("5%", 0.05m, IsDefault: false),
        new("0%", 0.00m, IsDefault: false),
      },
    };

  public IReadOnlyList<VatRateTemplate> GetForCountry(string countryCode) =>
    Catalogs.TryGetValue(countryCode, out var list) ? list : Array.Empty<VatRateTemplate>();

  public bool SupportsCountry(string countryCode) =>
    Catalogs.ContainsKey(countryCode);
}
