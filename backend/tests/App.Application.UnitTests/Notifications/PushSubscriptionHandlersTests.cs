using App.Application.Common.Interfaces;
using App.Application.Notifications.Push.Commands.SubscribePush;
using App.Application.Notifications.Push.Commands.UnsubscribePush;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-PUSH-SUB-* — subskrypcje Web Push: upsert po endpoincie, unsubscribe, oraz izolacja
/// tenanta (query filter na odczyt + write-guard na zapis obcego TenantId).
/// </summary>
public sealed class PushSubscriptionHandlersTests
{
  [Fact]
  public async Task SubscribePush_CreatesSubscription_ForCurrentTenantAndUser()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    using var db = NewDb(Guid.NewGuid().ToString(), tenantId);
    var handler = new SubscribePushCommandHandler(db, new FakeCurrentUser(userId), new FakeCurrentTenantService(tenantId));

    await handler.Handle(new SubscribePushCommand("https://push/ep", "p256", "auth"), ct);

    var row = Assert.Single(await db.PushSubscriptions.AsNoTracking().ToListAsync(ct));
    Assert.Equal(tenantId, row.TenantId);
    Assert.Equal(userId, row.UserId);
    Assert.Equal("https://push/ep", row.Endpoint);
  }

  [Fact]
  public async Task SubscribePush_SameEndpoint_Upserts_NoDuplicate()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    using var db = NewDb(Guid.NewGuid().ToString(), tenantId);
    var handler = new SubscribePushCommandHandler(db, new FakeCurrentUser(userId), new FakeCurrentTenantService(tenantId));

    await handler.Handle(new SubscribePushCommand("https://push/ep", "p1", "a1"), ct);
    await handler.Handle(new SubscribePushCommand("https://push/ep", "p2", "a2"), ct);

    var row = Assert.Single(await db.PushSubscriptions.AsNoTracking().ToListAsync(ct));
    Assert.Equal("p2", row.P256dh);
    Assert.Equal("a2", row.Auth);
  }

  [Fact]
  public async Task UnsubscribePush_RemovesOwnSubscription()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    using var db = NewDb(Guid.NewGuid().ToString(), tenantId);
    db.PushSubscriptions.Add(PushSubscription.Create(tenantId, Guid.NewGuid(), "https://push/ep", "p", "a", DateTime.UtcNow));
    await db.SaveChangesAsync(ct);

    var handler = new UnsubscribePushCommandHandler(db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new UnsubscribePushCommand("https://push/ep"), ct);

    Assert.Empty(await db.PushSubscriptions.AsNoTracking().ToListAsync(ct));
  }

  [Fact]
  public async Task UnsubscribePush_ForeignTenantEndpoint_IsNoop()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    // Seed obcej subskrypcji bez ambient-tenanta (guard SaveChanges nie odpala).
    using (var seed = NewDb(dbName, null))
    {
      seed.PushSubscriptions.Add(PushSubscription.Create(otherTenantId, Guid.NewGuid(), "https://push/foreign", "p", "a", DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new UnsubscribePushCommandHandler(db, new FakeCurrentTenantService(tenantId));
    // Query filter scope'uje do tenanta — obcy endpoint jest niewidoczny → no-op, nie kasuje.
    await handler.Handle(new UnsubscribePushCommand("https://push/foreign"), ct);

    using var check = NewDb(dbName, null);
    Assert.Single(await check.PushSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct));
  }

  [Fact]
  public async Task PushSubscription_WriteForOtherTenant_Throws_TenantViolation()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    using var db = NewDb(Guid.NewGuid().ToString(), tenantId);

    // Ambient tenant = tenantId, ale wiersz ma obcy TenantId → write-guard w SaveChanges.
    db.PushSubscriptions.Add(
      PushSubscription.Create(Guid.NewGuid(), Guid.NewGuid(), "https://push/ep", "p", "a", DateTime.UtcNow));

    await Assert.ThrowsAsync<TenantViolation>(() => db.SaveChangesAsync(ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static ApplicationDbContext NewDb(string dbName, Guid? currentTenant)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(dbName)
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(currentTenant));
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid? tenantId) => TenantId = tenantId;
  }

  private sealed class FakeCurrentUser : ICurrentUserAccessor
  {
    public FakeCurrentUser(Guid? userId) => UserId = userId;
    public Guid? UserId { get; }
    public Guid? CallerEmployeeId => null;
    public bool CanManageOtherEmployees => false;
    public bool IsSystemAdmin => false;
    public bool IsDeskAccount => false;
  }
}
