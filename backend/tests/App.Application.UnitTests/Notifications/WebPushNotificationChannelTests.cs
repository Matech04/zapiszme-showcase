using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Notifications.Push;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-WEBPUSH-* — kanał web-push wysyła na wszystkie subskrypcje odbiorcy (personel panelu),
/// pomija wiadomości dla klientów (bez konta), kasuje martwe subskrypcje (404/410).
/// </summary>
public sealed class WebPushNotificationChannelTests
{
  [Fact]
  public async Task SendAsync_WithoutRecipientUserId_DoesNotSend()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    var sender = new CapturingSender();
    var channel = new WebPushNotificationChannel(db, sender, NullLogger<WebPushNotificationChannel>.Instance);

    await channel.SendAsync(Message(tenantId, recipientUserId: null, referenceId: null), ct);

    Assert.Empty(sender.Sent);
  }

  [Fact]
  public async Task SendAsync_SendsToAllSubscriptionsOfRecipient_WithDeepLinkPayload()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var appointmentId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    Seed(db, tenantId, userId, "https://push/ep1");
    Seed(db, tenantId, userId, "https://push/ep2");
    Seed(db, tenantId, otherUser: Guid.NewGuid(), "https://push/other-user"); // inny odbiorca — pominięty
    await db.SaveChangesAsync(ct);

    var sender = new CapturingSender();
    var channel = new WebPushNotificationChannel(db, sender, NullLogger<WebPushNotificationChannel>.Instance);

    await channel.SendAsync(Message(tenantId, userId, appointmentId), ct);

    Assert.Equal(
      new[] { "https://push/ep1", "https://push/ep2" },
      sender.Sent.Select(s => s.Endpoint).OrderBy(e => e).ToArray());
    Assert.All(sender.Sent, s =>
      Assert.Contains($"/admin/schedule?appointment={appointmentId}", s.Payload));
  }

  [Fact]
  public async Task SendAsync_RemovesExpiredSubscriptions_KeepsLiveOnes()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    Seed(db, tenantId, userId, "https://push/live");
    Seed(db, tenantId, userId, "https://push/dead");
    await db.SaveChangesAsync(ct);

    var sender = new CapturingSender();
    sender.ResultByEndpoint["https://push/dead"] = WebPushSendResult.Expired;
    var channel = new WebPushNotificationChannel(db, sender, NullLogger<WebPushNotificationChannel>.Instance);

    await channel.SendAsync(Message(tenantId, userId, Guid.NewGuid()), ct);

    var remaining = await db.PushSubscriptions.AsNoTracking().Select(s => s.Endpoint).ToListAsync(ct);
    Assert.Equal(new[] { "https://push/live" }, remaining);
  }

  [Fact]
  public async Task SendAsync_NewBooking_BuildsFriendlyTitleAndBodyFromPayload()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    Seed(db, tenantId, userId, "https://push/ep1");
    await db.SaveChangesAsync(ct);

    var sender = new CapturingSender();
    var channel = new WebPushNotificationChannel(db, sender, NullLogger<WebPushNotificationChannel>.Instance);

    var msg = new NotificationMessage(
      tenantId, NotificationType.NewBookingToSalon,
      new NotificationRecipient("staff@e.co", null, userId, "Staff"),
      "Subject-fallback", "ShortText-fallback",
      new NotificationPayload(
        SalonName: "Salon", ServiceName: "Strzyżenie damskie",
        Date: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), StartTime: new TimeOnly(14, 0),
        CustomerFullName: "Anna Nowak"),
      Guid.NewGuid());

    await channel.SendAsync(msg, ct);

    var payload = Assert.Single(sender.Sent).Payload;
    Assert.Contains("Nowa rezerwacja — Anna Nowak", payload);
    Assert.Contains("Strzyżenie damskie", payload);
    Assert.DoesNotContain("Subject-fallback", payload); // treść z payloadu, nie z gotowego stringa
  }

  [Fact]
  public async Task SendAsync_CancellationWithoutCustomerName_OmitsDashKlient()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    using var db = NewDb(tenantId);
    Seed(db, tenantId, userId, "https://push/ep1");
    await db.SaveChangesAsync(ct);

    var sender = new CapturingSender();
    var channel = new WebPushNotificationChannel(db, sender, NullLogger<WebPushNotificationChannel>.Instance);

    // CustomerName = literał "Klient" (fallback braku nazwiska) — nie robimy „— Klient".
    var msg = new NotificationMessage(
      tenantId, NotificationType.CancellationToSalon,
      new NotificationRecipient("staff@e.co", null, userId, "Staff"),
      "S", "ST",
      new NotificationPayload(
        SalonName: "Salon", ServiceName: "Strzyżenie", CustomerName: "Klient",
        Date: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), StartTime: new TimeOnly(14, 0)),
      Guid.NewGuid());

    await channel.SendAsync(msg, ct);

    var payload = Assert.Single(sender.Sent).Payload;
    Assert.Contains("Odwołana wizyta", payload);
    Assert.DoesNotContain("Klient", payload);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static ApplicationDbContext NewDb(Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
  }

  private static void Seed(ApplicationDbContext db, Guid tenantId, Guid otherUser, string endpoint)
    => db.PushSubscriptions.Add(
      PushSubscription.Create(tenantId, otherUser, endpoint, "p256", "auth", DateTime.UtcNow));

  private static NotificationMessage Message(Guid tenantId, Guid? recipientUserId, Guid? referenceId) => new(
    tenantId,
    NotificationType.NewBookingToSalon,
    new NotificationRecipient("staff@e.co", null, recipientUserId, "Ann Smith"),
    "Nowa rezerwacja",
    "Klient zarezerwował wizytę",
    new NotificationPayload(SalonName: "Salon"),
    referenceId);

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class CapturingSender : IWebPushSender
  {
    public List<(string Endpoint, string Payload)> Sent { get; } = new();
    public Dictionary<string, WebPushSendResult> ResultByEndpoint { get; } = new();

    public Task<WebPushSendResult> SendAsync(
      string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct)
    {
      Sent.Add((endpoint, payloadJson));
      return Task.FromResult(
        ResultByEndpoint.TryGetValue(endpoint, out var r) ? r : WebPushSendResult.Delivered);
    }
  }
}
