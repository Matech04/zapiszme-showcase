using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-INAPP-* — kanał in-app persistuje powiadomienie i pushuje je w czasie rzeczywistym;
/// pomija wiadomości dla klientów (bez konta panelu).
/// </summary>
public sealed class InAppNotificationChannelTests
{
  [Fact]
  public async Task SendAsync_WithRecipientUserId_PersistsAndNotifies()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var appointmentId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    var realtime = new CapturingRealtimeNotifier();
    var channel = new InAppNotificationChannel(db, realtime, NullLogger<InAppNotificationChannel>.Instance);

    await channel.SendAsync(Message(tenantId, userId, appointmentId), ct);

    var row = Assert.Single(await db.Notifications.AsNoTracking().ToListAsync(ct));
    Assert.Equal(tenantId, row.TenantId);
    Assert.Equal(userId, row.RecipientUserId);
    Assert.Equal(NotificationType.NewBookingToSalon, row.Type);
    Assert.Equal("Temat", row.Subject);
    Assert.Equal(appointmentId, row.ReferenceId);
    Assert.Null(row.ReadAtUtc);

    var (notifiedTenant, notifiedUserId, dto) = Assert.Single(realtime.Calls);
    Assert.Equal(tenantId, notifiedTenant);
    // Realtime celuje w odbiorcę, nie w cały tenant — dzwonek jest osobisty.
    Assert.Equal(userId, notifiedUserId);
    Assert.Equal(row.Id, dto.Id);
    Assert.False(dto.Read);
  }

  [Fact]
  public async Task SendAsync_WithoutRecipientUserId_DoesNothing()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    var realtime = new CapturingRealtimeNotifier();
    var channel = new InAppNotificationChannel(db, realtime, NullLogger<InAppNotificationChannel>.Instance);

    await channel.SendAsync(Message(tenantId, recipientUserId: null, referenceId: null), ct);

    Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync(ct));
    Assert.Empty(realtime.Calls);
  }

  [Fact]
  public async Task SendAsync_PersistsEvenWhenRealtimeThrows()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    var channel = new InAppNotificationChannel(db, new ThrowingRealtimeNotifier(), NullLogger<InAppNotificationChannel>.Instance);

    // Kanał propaguje wyjątek (dispatcher go izoluje), ale wiersz musi zostać zapisany wcześniej.
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => channel.SendAsync(Message(tenantId, Guid.NewGuid(), Guid.NewGuid()), ct));

    Assert.Single(await db.Notifications.AsNoTracking().ToListAsync(ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static ApplicationDbContext NewDb(Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
  }

  private static NotificationMessage Message(Guid tenantId, Guid? recipientUserId, Guid? referenceId) => new(
    tenantId,
    NotificationType.NewBookingToSalon,
    new NotificationRecipient("staff@e.co", null, recipientUserId, "Ann Smith"),
    "Temat",
    "Krótki tekst",
    new NotificationPayload(SalonName: "Salon"),
    referenceId);

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class CapturingRealtimeNotifier : IRealtimeNotifier
  {
    public List<(Guid TenantId, Guid? RecipientUserId, RealtimeNotificationDto Dto)> Calls { get; } = new();
    public Task NotifyRecipientAsync(
      Guid tenantId, Guid? recipientUserId, RealtimeNotificationDto notification, CancellationToken ct)
    {
      Calls.Add((tenantId, recipientUserId, notification));
      return Task.CompletedTask;
    }
  }

  private sealed class ThrowingRealtimeNotifier : IRealtimeNotifier
  {
    public Task NotifyRecipientAsync(
      Guid tenantId, Guid? recipientUserId, RealtimeNotificationDto notification, CancellationToken ct)
      => throw new InvalidOperationException("boom");
  }
}
