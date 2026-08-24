using App.Application.Common.Interfaces;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Common;

/// <summary>
/// NOTIF-CLEANUP-001..004 — cykl usuwania starych powiadomień in-app po oknie retencji.
///
/// Wzywamy bezpośrednio statyczny entry-point <c>NotificationCleanupHostedService.RunCycleAsync</c>.
/// Job leci bez kontekstu tenanta (<c>IgnoreQueryFilters</c>), więc czyści wszystkie tenanty,
/// niezależnie od stanu „przeczytane".
/// </summary>
public sealed class NotificationCleanupTests
{
  private static readonly TimeSpan Retention = TimeSpan.FromDays(14);
  private static readonly DateTime UtcNow = new(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

  [Fact]
  public async Task Notification_older_than_retention_is_deleted()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var old = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(15));

    var deleted = await NotificationCleanupHostedService.RunCycleAsync(
      db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(1, deleted);
    Assert.False(await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Id == old, ct));
  }

  [Fact]
  public async Task Notification_within_retention_is_kept()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var fresh = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(13));

    var deleted = await NotificationCleanupHostedService.RunCycleAsync(
      db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(0, deleted);
    Assert.True(await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Id == fresh, ct));
  }

  [Fact]
  public async Task Old_notification_is_deleted_even_when_already_read()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var read = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(30), read: true);
    var unread = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(30), read: false);

    var deleted = await NotificationCleanupHostedService.RunCycleAsync(
      db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(2, deleted);
    Assert.False(await db.Notifications.IgnoreQueryFilters()
      .AnyAsync(n => n.Id == read || n.Id == unread, ct));
  }

  [Fact]
  public async Task Cleanup_spans_all_tenants_without_tenant_context()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var tenantA = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(20), tenantId: Guid.NewGuid());
    var tenantB = SeedNotification(db, createdAt: UtcNow - TimeSpan.FromDays(20), tenantId: Guid.NewGuid());

    var deleted = await NotificationCleanupHostedService.RunCycleAsync(
      db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(2, deleted);
    Assert.False(await db.Notifications.IgnoreQueryFilters()
      .AnyAsync(n => n.Id == tenantA || n.Id == tenantB, ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static Guid SeedNotification(
    ApplicationDbContext db,
    DateTime createdAt,
    bool read = false,
    Guid? tenantId = null)
  {
    var n = Notification.Create(
      tenantId ?? Guid.NewGuid(),
      recipientUserId: null,
      NotificationType.NewBookingToSalon,
      subject: "Nowa rezerwacja",
      shortText: "Klient zarezerwował wizytę",
      referenceId: null,
      nowUtc: createdAt);
    if (read)
    {
      n.MarkRead(createdAt);
    }
    db.Notifications.Add(n);
    db.SaveChanges();
    return n.Id;
  }

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
