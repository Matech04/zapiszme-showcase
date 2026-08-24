using App.Application.Common.Interfaces;
using App.Application.Subscription.Commands.ChangeSeats;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Subscription;

/// <summary>
/// APP-SUB — handler ChangeSeatsCommand: happy path, downgrade, walidacja, TenantViolation.
/// </summary>
public sealed class ChangeSeatsHandlerTests
{
  [Fact]
  public async Task ChangeSeats_updates_subscription_seats_and_returns_new_price()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    tenant.Subscription.Activate(seats: 1, foundingMember: false);
    await db.SaveChangesAsync(ct);

    var handler = new ChangeSeatsCommandHandler(db, db, new FakeCurrentTenantService(tenant.Id));

    var result = await handler.Handle(new ChangeSeatsCommand(3), ct);

    Assert.Equal(3, result.Seats);
    // 79 + 35*2 = 149 zł
    Assert.Equal(14900, result.MonthlyPriceInGrosze);
    Assert.Equal(500, result.MonthlySmsAllowance);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.Equal(3, reloaded!.Subscription.Seats);
  }

  [Fact]
  public async Task ChangeSeats_supports_downgrade()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    tenant.Subscription.Activate(seats: 5, foundingMember: false);
    await db.SaveChangesAsync(ct);

    var handler = new ChangeSeatsCommandHandler(db, db, new FakeCurrentTenantService(tenant.Id));

    var result = await handler.Handle(new ChangeSeatsCommand(2), ct);

    Assert.Equal(2, result.Seats);
  }

  [Fact]
  public async Task ChangeSeats_throws_when_caller_tenant_does_not_match()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _) = SetupDb();
    // Caller in different tenant — TenantId in current service != tenant.Id from DB
    var fakeOtherTenantId = Guid.NewGuid();
    var handler = new ChangeSeatsCommandHandler(db, db, new FakeCurrentTenantService(fakeOtherTenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ChangeSeatsCommand(2), ct));
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────────

  private static (ApplicationDbContext db, Tenant tenant) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var tenant = new Tenant("Test Salon", "test-" + Guid.NewGuid().ToString("N")[..6]);
    typeof(Entity).GetProperty("Id")!.SetValue(tenant, tenantId);
    db.Tenants.Add(tenant);
    db.SaveChanges();
    return (db, tenant);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
