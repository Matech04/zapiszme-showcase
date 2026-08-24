using App.Application.Common.Interfaces;

namespace App.Application.Common;

/// <summary>
/// Dodaje do <see cref="IApplicationDbContext.VatRates"/> domyślny zestaw stawek dla nowego tenanta
/// na podstawie kodu kraju. Nie woła SaveChanges — caller decyduje, w jakiej transakcji
/// zostaną utrwalone (rejestracja właściciela osadza to w tej samej transakcji co Tenant/Employee).
/// </summary>
public interface ITenantVatRateSeeder
{
  void SeedDefaults(Guid tenantId, string countryCode);
}

public sealed class TenantVatRateSeeder : ITenantVatRateSeeder
{
  private readonly IApplicationDbContext _context;
  private readonly IVatRateCatalog _catalog;

  public TenantVatRateSeeder(IApplicationDbContext context, IVatRateCatalog catalog)
  {
    _context = context;
    _catalog = catalog;
  }

  public void SeedDefaults(Guid tenantId, string countryCode)
  {
    foreach (var template in _catalog.GetForCountry(countryCode))
    {
      _context.VatRates.Add(new VatRate(tenantId, template.Name, template.Value, template.IsDefault));
    }
  }
}
