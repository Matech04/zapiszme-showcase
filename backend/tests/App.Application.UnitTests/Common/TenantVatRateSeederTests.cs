using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Common;

/// <summary>
/// VAT-CAT-002 — TenantVatRateSeeder: aplikuje katalog do bazy.
/// </summary>
public sealed class TenantVatRateSeederTests
{
  [Fact]
  public async Task SeedDefaults_for_PL_adds_full_polish_catalog_to_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var seeder = new TenantVatRateSeeder(db, new VatRateCatalog());

    seeder.SeedDefaults(tenantId, "PL");
    await db.SaveChangesAsync(ct);

    var saved = await db.VatRates
      .IgnoreQueryFilters()
      .Where(v => v.TenantId == tenantId)
      .AsNoTracking()
      .ToListAsync(ct);

    Assert.Equal(5, saved.Count);
    Assert.Contains(saved, v => v.Name == "zw." && v.Value == 0.00m && v.IsDefault);
    Assert.Single(saved, v => v.IsDefault);
  }

  [Fact]
  public async Task SeedDefaults_for_unknown_country_is_noop()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var seeder = new TenantVatRateSeeder(db, new VatRateCatalog());

    seeder.SeedDefaults(tenantId, "XX");
    await db.SaveChangesAsync(ct);

    var count = await db.VatRates.IgnoreQueryFilters().CountAsync(ct);
    Assert.Equal(0, count);
  }

  // Seeder jest wywoływany z anonimowego kontekstu rejestracji (currentTenant = null),
  // co odbywa się PRZED utrwaleniem aktywnej sesji tenanta. Sprawdzamy że dwa
  // niezależne wywołania nie kolidują (osobne tenant.Id, każdy dostaje pełen katalog).
  [Fact]
  public async Task SeedDefaults_for_different_tenants_isolates_data_under_anonymous_context()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var seeder = new TenantVatRateSeeder(db, new VatRateCatalog());

    seeder.SeedDefaults(tenantA, "PL");
    seeder.SeedDefaults(tenantB, "PL");
    await db.SaveChangesAsync(ct);

    var aCount = await db.VatRates.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantA, ct);
    var bCount = await db.VatRates.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantB, ct);
    Assert.Equal(5, aCount);
    Assert.Equal(5, bCount);
  }

  private static (ApplicationDbContext db, Guid tenantId) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    return (db, tenantId);
  }

  private static ApplicationDbContext SetupAnonymousDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new AnonymousCurrentTenantService());
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class AnonymousCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
