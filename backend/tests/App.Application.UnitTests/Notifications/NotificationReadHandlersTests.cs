using App.Application.Common.Interfaces;
using App.Application.Notifications.Commands.MarkAllNotificationsRead;
using App.Application.Notifications.Commands.MarkNotificationRead;
using App.Application.Notifications.Queries.GetNotifications;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-READ-* — query listy powiadomień i komendy oznaczania jako przeczytane; izolacja tenanta
/// ORAZ izolacja per użytkownik (dzwonek jest osobisty; wyjątek: konto „Recepcja"/Kiosk widzi
/// powiadomienia całego salonu). Seedowanie wielotenantowe idzie przez kontekst bez
/// ambient-tenanta (guard w SaveChanges odpala się tylko gdy ambient tenant jest ustawiony).
/// </summary>
public sealed class NotificationReadHandlersTests
{
  private static readonly Guid Me = Guid.NewGuid();
  private static readonly Guid Colleague = Guid.NewGuid();

  [Fact]
  public async Task GetNotifications_ReturnsOnlyOwn_OrderedDesc_WithReadFlag()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      var older = Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "Stare", "...", null, DateTime.UtcNow.AddMinutes(-10));
      var newer = Notification.Create(tenantId, Me, NotificationType.CancellationToSalon, "Nowe", "...", null, DateTime.UtcNow);
      newer.MarkRead(DateTime.UtcNow);
      var colleagues = Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "Koleżanki", "...", null, DateTime.UtcNow);
      var foreign = Notification.Create(otherTenantId, Me, NotificationType.NewBookingToSalon, "Obce", "...", null, DateTime.UtcNow);
      seed.Notifications.AddRange(older, newer, colleagues, foreign);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new GetNotificationsQueryHandler(db, new FakeCurrentTenantService(tenantId), Staff(Me));
    var result = await handler.Handle(new GetNotificationsQuery(), ct);

    Assert.Equal(2, result.Count);
    Assert.Equal("Nowe", result[0].Subject);  // desc by CreatedAtUtc
    Assert.True(result[0].Read);
    Assert.Equal("Stare", result[1].Subject);
    Assert.False(result[1].Read);
    Assert.DoesNotContain(result, n => n.Subject == "Koleżanki");
  }

  [Fact]
  public async Task GetNotifications_DeskAccount_SeesWholeSalon()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.AddRange(
        Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "Moje", "...", null, DateTime.UtcNow.AddMinutes(-1)),
        Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "Koleżanki", "...", null, DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var kioskUserId = Guid.NewGuid();
    var handler = new GetNotificationsQueryHandler(db, new FakeCurrentTenantService(tenantId), Desk(kioskUserId));
    var result = await handler.Handle(new GetNotificationsQuery(), ct);

    Assert.Equal(2, result.Count);
  }

  [Fact]
  public async Task GetNotifications_AuthenticatedUserWithoutUserId_ReturnsEmpty_NotOthers()
  {
    // Fail-closed (gałąź bezpieczeństwa w GetNotificationsQuery): uwierzytelnione żądanie bez
    // UserId, ale NIE-Desk, dostaje pustą listę zamiast cudzych powiadomień. Wcześniej niepokryte
    // — FakeCurrentUser w pozostałych testach zawsze miał niepuste UserId.
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.AddRange(
        Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "Moje", "...", null, DateTime.UtcNow),
        Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "Koleżanki", "...", null, DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var noUser = new FakeCurrentUser(userId: null, isDesk: false);
    var handler = new GetNotificationsQueryHandler(db, new FakeCurrentTenantService(tenantId), noUser);
    var result = await handler.Handle(new GetNotificationsQuery(), ct);

    Assert.Empty(result);
  }

  [Fact]
  public async Task GetNotifications_SupportSession_SeesWholeSalon()
  {
    // Tryb wsparcia (Admin w impersonacji) widzi powiadomienia CAŁEGO salonu jak Recepcja —
    // do diagnostyki. `Support` ma inne UserId niż odbiorcy w salonie, więc gdyby zadziałał filtr
    // osobisty, lista byłaby pusta; oczekujemy obu wierszy. Zakres i tak scope'uje query filter.
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.AddRange(
        Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "Pracownicy A", "...", null, DateTime.UtcNow.AddMinutes(-1)),
        Notification.Create(tenantId, Colleague, NotificationType.CancellationToSalon, "Pracownicy B", "...", null, DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new GetNotificationsQueryHandler(db, new FakeCurrentTenantService(tenantId), Support(Guid.NewGuid()));
    var result = await handler.Handle(new GetNotificationsQuery(), ct);

    Assert.Equal(2, result.Count);
  }

  [Fact]
  public async Task MarkNotificationRead_SetsReadAt_AndIsIdempotent()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();
    var notification = Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "N", "...", null, DateTime.UtcNow);

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.Add(notification);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new MarkNotificationReadCommandHandler(db, new FakeCurrentTenantService(tenantId), Staff(Me));
    await handler.Handle(new MarkNotificationReadCommand(notification.Id), ct);
    var firstReadAt = (await db.Notifications.AsNoTracking().FirstAsync(ct)).ReadAtUtc;
    Assert.NotNull(firstReadAt);

    await handler.Handle(new MarkNotificationReadCommand(notification.Id), ct);
    var secondReadAt = (await db.Notifications.AsNoTracking().FirstAsync(ct)).ReadAtUtc;
    Assert.Equal(firstReadAt, secondReadAt);  // idempotentne — nie nadpisuje
  }

  [Fact]
  public async Task MarkNotificationRead_ForeignId_IsNoOp()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();
    var foreign = Notification.Create(Guid.NewGuid(), Me, NotificationType.NewBookingToSalon, "Obce", "...", null, DateTime.UtcNow);

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.Add(foreign);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new MarkNotificationReadCommandHandler(db, new FakeCurrentTenantService(tenantId), Staff(Me));
    await handler.Handle(new MarkNotificationReadCommand(foreign.Id), ct);

    using var verify = NewDb(dbName, null);
    Assert.Null((await verify.Notifications.IgnoreQueryFilters().AsNoTracking().FirstAsync(ct)).ReadAtUtc);
  }

  [Fact]
  public async Task MarkNotificationRead_OtherStaffNotification_IsNoOp()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();
    var colleagues = Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "Koleżanki", "...", null, DateTime.UtcNow);

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.Add(colleagues);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new MarkNotificationReadCommandHandler(db, new FakeCurrentTenantService(tenantId), Staff(Me));
    await handler.Handle(new MarkNotificationReadCommand(colleagues.Id), ct);

    using var verify = NewDb(dbName, null);
    Assert.Null((await verify.Notifications.IgnoreQueryFilters().AsNoTracking().FirstAsync(ct)).ReadAtUtc);
  }

  [Fact]
  public async Task MarkAllNotificationsRead_MarksOnlyOwn_NotColleagues()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.AddRange(
        Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "A", "...", null, DateTime.UtcNow),
        Notification.Create(tenantId, Me, NotificationType.CancellationToSalon, "B", "...", null, DateTime.UtcNow),
        Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "C", "...", null, DateTime.UtcNow),
        Notification.Create(otherTenantId, Me, NotificationType.NewBookingToSalon, "D", "...", null, DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new MarkAllNotificationsReadCommandHandler(db, new FakeCurrentTenantService(tenantId), Staff(Me));
    await handler.Handle(new MarkAllNotificationsReadCommand(), ct);

    using var verify = NewDb(dbName, null);
    var all = await verify.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
    Assert.All(all.Where(n => n.TenantId == tenantId && n.RecipientUserId == Me), n => Assert.NotNull(n.ReadAtUtc));
    // Pracownik nie gasi dzwonka koleżance ani w obcym tenancie.
    Assert.Null(all.Single(n => n.RecipientUserId == Colleague).ReadAtUtc);
    Assert.Null(all.Single(n => n.TenantId == otherTenantId).ReadAtUtc);
  }

  [Fact]
  public async Task MarkAllNotificationsRead_DeskAccount_MarksWholeSalon()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      seed.Notifications.AddRange(
        Notification.Create(tenantId, Me, NotificationType.NewBookingToSalon, "A", "...", null, DateTime.UtcNow),
        Notification.Create(tenantId, Colleague, NotificationType.NewBookingToSalon, "B", "...", null, DateTime.UtcNow));
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new MarkAllNotificationsReadCommandHandler(
      db, new FakeCurrentTenantService(tenantId), Desk(Guid.NewGuid()));
    await handler.Handle(new MarkAllNotificationsReadCommand(), ct);

    using var verify = NewDb(dbName, null);
    var all = await verify.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
    Assert.All(all, n => Assert.NotNull(n.ReadAtUtc));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static FakeCurrentUser Staff(Guid userId) => new(userId, isDesk: false);
  private static FakeCurrentUser Desk(Guid userId) => new(userId, isDesk: true);

  /// <summary>Admin w trybie wsparcia (impersonacja salonu) — nie-Desk, ale widzi cały salon.</summary>
  private static FakeCurrentUser Support(Guid adminUserId) => new(adminUserId, isDesk: false, isSupport: true);

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
    public FakeCurrentUser(Guid? userId, bool isDesk, bool isSupport = false)
    {
      UserId = userId;
      IsDeskAccount = isDesk;
      IsSupportSession = isSupport;
    }

    public Guid? UserId { get; }
    public Guid? CallerEmployeeId => null;
    public bool CanManageOtherEmployees => false;
    public bool IsSystemAdmin => false;
    public bool IsDeskAccount { get; }
    public bool IsSupportSession { get; }
  }
}
