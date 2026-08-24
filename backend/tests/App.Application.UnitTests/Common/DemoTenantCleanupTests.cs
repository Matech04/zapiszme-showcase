using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Common;

/// <summary>
/// DEMO-CLEANUP-001..003 — cykl hard-delete efemerycznych demo-tenantów po TTL.
///
/// Wzywamy bezpośrednio statyczny entry-point <c>DemoTenantCleanupHostedService.RunCycleAsync</c>.
/// Dane bez wizyt (owned-types Money łamią EF InMemory) — pełny graf pokrywa test integracyjny.
/// </summary>
public sealed class DemoTenantCleanupTests
{
  private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
  private static readonly DateTime UtcNow = new(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

  [Fact]
  public async Task Expired_demo_tenant_is_hard_deleted_with_user_employee_and_vat_rates()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var (userId, tenantId) = SeedDemoTenant(db, "demo-old@demo.local", createdAt: UtcNow - TimeSpan.FromHours(48));

    await DemoTenantCleanupHostedService.RunCycleAsync(db, UtcNow, Ttl, ct, NullLogger.Instance);

    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId, ct));
    Assert.False(await db.VatRates.IgnoreQueryFilters().AnyAsync(v => v.TenantId == tenantId, ct));
    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
  }

  [Fact]
  public async Task Fresh_demo_tenant_within_ttl_is_kept()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var (userId, tenantId) = SeedDemoTenant(db, "demo-fresh@demo.local", createdAt: UtcNow - TimeSpan.FromHours(2));

    await DemoTenantCleanupHostedService.RunCycleAsync(db, UtcNow, Ttl, ct, NullLogger.Instance);

    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
  }

  [Fact]
  public async Task Non_demo_tenant_is_never_deleted_even_if_ancient()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();

    // Zwykły (nie-demo) tenant, „utworzony" dawno — cleanup demo nie może go ruszyć.
    var user = NewUser("real@e.local");
    var tenant = new Tenant("Prawdziwy Salon", "real-salon");
    var employee = new Employee(tenant.Id, user.Id, "Jan", "Kowalski", user.Email!);
    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    new TenantVatRateSeeder(db, new VatRateCatalog()).SeedDefaults(tenant.Id, "PL");
    db.SaveChanges();

    await DemoTenantCleanupHostedService.RunCycleAsync(db, UtcNow, Ttl, ct, NullLogger.Instance);

    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant.Id, ct));
    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id, ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static (Guid userId, Guid tenantId) SeedDemoTenant(
    ApplicationDbContext db, string email, DateTime createdAt)
  {
    var user = NewUser(email);
    var tenant = new Tenant("Studio Demo", email.Replace("@", "-").Replace(".", "-"));
    tenant.MarkAsDemo(createdAt); // ustala DemoCreatedAtUtc → sterownik TTL
    tenant.StartTrial();
    var employee = new Employee(tenant.Id, user.Id, "Ania", "Demo", email);

    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    new TenantVatRateSeeder(db, new VatRateCatalog()).SeedDefaults(tenant.Id, "PL");
    db.SaveChanges();
    return (user.Id, tenant.Id);
  }

  private static User NewUser(string email) => new(email, email)
  {
    EmailConfirmed = true,
    PhoneNumberConfirmed = true,
    NormalizedEmail = email.ToUpperInvariant(),
    NormalizedUserName = email.ToUpperInvariant(),
    SecurityStamp = Guid.NewGuid().ToString(),
    ConcurrencyStamp = Guid.NewGuid().ToString(),
  };

  private static ApplicationDbContext SetupAnonymousDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new AnonymousCurrentTenantService());
  }

  private sealed class AnonymousCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
