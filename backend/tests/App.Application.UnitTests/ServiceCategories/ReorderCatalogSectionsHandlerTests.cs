using App.Application.Common.Interfaces;
using App.Application.ServiceCategories.Commands.ReorderCatalogSections;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.ServiceCategories;

/// <summary>
/// Unified-reorder katalogu: realne kategorie + wirtualna sekcja „Bez kategorii" (null) zapisywane
/// w jednej sekwencji. Pozycja na liście = OrderIndex (kategoria) / UncategorizedOrderIndex (tenant).
/// Pokrywa: poprawne przypisanie indeksów z null w środku, NotFound dla obcego/nieistniejącego Id,
/// write-side TenantViolation guard przy próbie zapisu cudzej kategorii.
/// </summary>
public sealed class ReorderCatalogSectionsHandlerTests
{
  [Fact]
  public async Task Handle_AssignsIndicesToCategoriesAndTenant_WithNullInMiddle()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);

    var catA = new ServiceCategory(tenant.Id, "A", 0);
    var catB = new ServiceCategory(tenant.Id, "B", 0);
    db.ServiceCategories.AddRange(catA, catB);
    await db.SaveChangesAsync(ct);

    // Docelowa kolejność: [A, „Bez kategorii", B] → A=0, uncategorized=1, B=2.
    var command = new ReorderCatalogSectionsCommand(new Guid?[] { catA.Id, null, catB.Id });
    await Handler(db, tenant.Id).Handle(command, ct);

    var reloadedA = await db.ServiceCategories.AsNoTracking().FirstAsync(c => c.Id == catA.Id, ct);
    var reloadedB = await db.ServiceCategories.AsNoTracking().FirstAsync(c => c.Id == catB.Id, ct);
    var reloadedTenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id, ct);

    Assert.Equal(0, reloadedA.OrderIndex);
    Assert.Equal(2, reloadedB.OrderIndex);
    Assert.Equal(1, reloadedTenant.UncategorizedOrderIndex);
  }

  [Fact]
  public async Task Handle_WithoutNull_DoesNotTouchUncategorizedOrder()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);

    var catA = new ServiceCategory(tenant.Id, "A", 0);
    var catB = new ServiceCategory(tenant.Id, "B", 0);
    db.ServiceCategories.AddRange(catA, catB);
    await db.SaveChangesAsync(ct);

    var command = new ReorderCatalogSectionsCommand(new Guid?[] { catB.Id, catA.Id });
    await Handler(db, tenant.Id).Handle(command, ct);

    var reloadedTenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenant.Id, ct);
    // Brak null → sekcja „Bez kategorii" zostaje na domyślnej pozycji (na końcu).
    Assert.Equal(Tenant.UncategorizedOrderDefault, reloadedTenant.UncategorizedOrderIndex);
  }

  [Fact]
  public async Task Handle_UnknownCategoryId_ThrowsNotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var command = new ReorderCatalogSectionsCommand(new Guid?[] { Guid.NewGuid() });

    await Assert.ThrowsAsync<NotFoundException>(() => Handler(db, tenant.Id).Handle(command, ct));
  }

  [Fact]
  public async Task Handle_ForeignTenantCategoryId_ThrowsNotFound_FilteredByQueryFilter()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantA = new Tenant("Salon A", "salon-a");
    var tenantB = new Tenant("Salon B", "salon-b");
    var dbName = Guid.NewGuid().ToString();

    Guid foreignCategoryId;
    await using (var dbB = NewDb(dbName, tenantB.Id))
    {
      dbB.Tenants.Add(tenantB);
      var foreign = new ServiceCategory(tenantB.Id, "Obca", 0);
      dbB.ServiceCategories.Add(foreign);
      await dbB.SaveChangesAsync(ct);
      foreignCategoryId = foreign.Id;
    }

    await using var dbA = NewDb(dbName, tenantA.Id);
    dbA.Tenants.Add(tenantA);
    await dbA.SaveChangesAsync(ct);

    // W kontekście A: kategoria B wycięta przez query filter → kompletność zawiedzie → NotFound.
    var command = new ReorderCatalogSectionsCommand(new Guid?[] { foreignCategoryId });

    await Assert.ThrowsAsync<NotFoundException>(() => Handler(dbA, tenantA.Id).Handle(command, ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static ReorderCatalogSectionsHandler Handler(ApplicationDbContext db, Guid tenantId) =>
    new(db, new TenantRepository(db), db, new FakeCurrentTenantService(tenantId));

  private static ApplicationDbContext NewDb(Guid tenantId) => NewDb(Guid.NewGuid().ToString(), tenantId);

  private static ApplicationDbContext NewDb(string databaseName, Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(databaseName)
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
