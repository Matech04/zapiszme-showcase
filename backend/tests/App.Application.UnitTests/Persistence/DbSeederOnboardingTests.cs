using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Persistence;

/// <summary>
/// Salony demo muszą być stemplowane jako „po kreatorze". Bez tego <c>onboardingGuard</c> na
/// <c>/admin/**</c> wypycha KAŻDE logowanie na konto demo do kreatora zakładania salonu — dokładnie
/// tak, jak zgłoszono po zasianiu local-proda.
///
/// Regresja była subtelna: migracja <c>AddOnboardingFields</c> backfilluje istniejące tenanty
/// (<c>UPDATE ... WHERE onboarding_completed_at IS NULL</c>), więc na bazie, która powstała PRZED
/// onboardingiem, wszystko działało. Na świeżej bazie backfill aktualizuje zero wierszy, bo seed
/// wstawia salony dopiero po migracjach — i każdy demo-tenant zostawał z NULL-em.
/// </summary>
public sealed class DbSeederOnboardingTests
{
  private static readonly Guid SalonATenantId = new("00000000-0000-0000-0000-000000000001");
  private static readonly Guid ForeignTenantId = new("99999999-9999-9999-9999-999999999999");

  [Fact]
  public async Task Dosiew_stempluje_salon_demo_ktory_zostal_bez_onboardingu()
  {
    var ct = TestContext.Current.CancellationToken;
    await using var db = CreateContext();

    db.Tenants.Add(CreateTenant(SalonATenantId, "Salon A", "salon-a"));
    await db.SaveChangesAsync(ct);

    await DbSeeder.SeedAsync(db);

    var salonA = await db.Tenants.SingleAsync(t => t.Id == SalonATenantId, ct);
    Assert.NotNull(salonA.OnboardingCompletedAt);
  }

  [Fact]
  public async Task Dosiew_nie_rusza_tenantow_spoza_seedu_demo()
  {
    var ct = TestContext.Current.CancellationToken;
    await using var db = CreateContext();

    // Realny salon klienta, który celowo jest w trakcie kreatora. Seed nie ma prawa go „dokończyć",
    // nawet gdy ktoś odpali go na bazie z prawdziwymi danymi.
    db.Tenants.Add(CreateTenant(ForeignTenantId, "Salon klienta", "salon-klienta"));
    await db.SaveChangesAsync(ct);

    await DbSeeder.SeedAsync(db);

    var foreign = await db.Tenants.SingleAsync(t => t.Id == ForeignTenantId, ct);
    Assert.Null(foreign.OnboardingCompletedAt);
  }

  [Fact]
  public async Task Dosiew_nie_przestawia_juz_ustawionego_stempla()
  {
    var ct = TestContext.Current.CancellationToken;
    await using var db = CreateContext();

    var stamped = CreateTenant(SalonATenantId, "Salon A", "salon-a");
    var original = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    stamped.MarkOnboardingCompleted(original);
    db.Tenants.Add(stamped);
    await db.SaveChangesAsync(ct);

    await DbSeeder.SeedAsync(db);

    var salonA = await db.Tenants.SingleAsync(t => t.Id == SalonATenantId, ct);
    Assert.Equal(original, salonA.OnboardingCompletedAt);
  }

  private static Tenant CreateTenant(Guid id, string name, string slug)
  {
    var tenant = new Tenant(name, slug, "Europe/Warsaw", "PLN");
    typeof(Entity).GetProperty("Id")!.SetValue(tenant, id);
    return tenant;
  }

  private static ApplicationDbContext CreateContext()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(SalonATenantId));
  }

  private sealed class FakeCurrentTenantService(Guid tenantId) : ICurrentTenantService
  {
    public Guid? TenantId { get; set; } = tenantId;
  }
}
