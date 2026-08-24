using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// SMS-CAP-* — guard liczy udane SMS bieżącego miesiąca per tenant i blokuje na/po efektywnym limicie.
/// </summary>
public sealed class SmsUsageGuardTests
{
  [Fact]
  public async Task WithinCap_WhenUnderLimit_ReturnsTrue()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio"); // limit z planu = 200
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await SeedSms(db, tenant.Id, count: 5, ct);

    Assert.True(await new SmsUsageGuard(db).IsWithinMonthlyCapAsync(tenant.Id, ct));
  }

  [Fact]
  public async Task WithinCap_AtAndOverHardCap_ReturnsFalse()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    tenant.Subscription.SetMonthlySmsHardCap(3); // override admina
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await SeedSms(db, tenant.Id, count: 3, ct); // dokładnie limit

    // 3 >= 3 → poza limitem (blokujemy kolejny).
    Assert.False(await new SmsUsageGuard(db).IsWithinMonthlyCapAsync(tenant.Id, ct));
  }

  [Fact]
  public async Task WithinCap_CountsOnlyCurrentTenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantA = new Tenant("A", "a");
    tenantA.Subscription.SetMonthlySmsHardCap(2);
    var tenantB = new Tenant("B", "b");
    var dbName = Guid.NewGuid().ToString();

    await using (var dbA = NewDb(dbName, tenantA.Id))
    {
      dbA.Tenants.Add(tenantA);
      await SeedSms(dbA, tenantA.Id, count: 1, ct);
    }
    await using (var dbB = NewDb(dbName, tenantB.Id))
    {
      dbB.Tenants.Add(tenantB);
      await SeedSms(dbB, tenantB.Id, count: 50, ct); // dużo u B nie wpływa na A
    }

    await using var dbReadA = NewDb(dbName, tenantA.Id);
    // A ma 1 z limitu 2 → wciąż w limicie mimo 50 SMS u B.
    Assert.True(await new SmsUsageGuard(dbReadA).IsWithinMonthlyCapAsync(tenantA.Id, ct));
  }

  [Fact]
  public async Task WithinCap_FailedSmsDoNotCount()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    tenant.Subscription.SetMonthlySmsHardCap(2);
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    // 5 nieudanych SMS — nie liczą się do limitu.
    for (var i = 0; i < 5; i++)
    {
      db.NotificationUsage.Add(NotificationUsageRecord.ForSms(
        tenant.Id, NotificationType.AppointmentReminderToCustomer, success: false, 0m, DateTime.UtcNow));
    }
    await db.SaveChangesAsync(ct);

    Assert.True(await new SmsUsageGuard(db).IsWithinMonthlyCapAsync(tenant.Id, ct));
  }

  // ── helpers ───────────────────────────────────────────────────────────────

  private static async Task SeedSms(ApplicationDbContext db, Guid tenantId, int count, CancellationToken ct)
  {
    for (var i = 0; i < count; i++)
    {
      db.NotificationUsage.Add(NotificationUsageRecord.ForSms(
        tenantId, NotificationType.AppointmentReminderToCustomer, success: true, 1m, DateTime.UtcNow));
    }
    await db.SaveChangesAsync(ct);
  }

  private static ApplicationDbContext NewDb(Guid tenantId) => NewDb(Guid.NewGuid().ToString(), tenantId);

  private static ApplicationDbContext NewDb(string databaseName, Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(databaseName)
      .Options;
    return new ApplicationDbContext(options, new FakeTenant(tenantId));
  }

  private sealed class FakeTenant : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeTenant(Guid id) => TenantId = id;
  }
}
