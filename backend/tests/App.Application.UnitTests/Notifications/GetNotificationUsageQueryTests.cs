using App.Application.Common.Interfaces;
using App.Application.Notifications.Queries.GetNotificationUsage;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-USAGE-* — query agreguje zużycie SMS/e-mail w miesiącu, scope'uje po tenancie i miesiącu,
/// liczy limit z subskrypcji oraz nadwyżkę. Recorder dopisuje wiersze do dziennika.
/// </summary>
public sealed class GetNotificationUsageQueryTests
{
  [Fact]
  public async Task Handle_AggregatesSmsAndEmail_WithAllowanceAndRemaining()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var month = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    db.NotificationUsage.AddRange(
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 1m, month),
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 2m, month),
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.BookingConfirmationToCustomer, false, 0m, month),
      NotificationUsageRecord.ForEmail(tenant.Id, NotificationType.NewBookingToSalon, true, month));
    await db.SaveChangesAsync(ct);

    var result = await Handler(db, tenant.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);

    Assert.Equal(2, result.SmsSent);
    Assert.Equal(1, result.SmsFailed);
    Assert.Equal(3m, result.SmsPoints);
    // 2 wiadomości, 3 punkty → wykorzystane = max(2, ceil(3)) = 3 kredyty.
    Assert.Equal(3, result.SmsCreditsUsed);
    Assert.Equal(1, result.EmailSent);
    Assert.Equal(App.Domain.Aggregates.TenantAggregate.Subscription.BaseSmsAllowance, result.SmsAllowance);
    Assert.Equal(App.Domain.Aggregates.TenantAggregate.Subscription.BaseSmsAllowance - 3, result.SmsRemaining);
    Assert.Equal(0, result.SmsOverageCount);
    Assert.Equal(0, result.OverageCostGrosze);
  }

  [Fact]
  public async Task Handle_CountsMultiSegmentSmsByPoints_AndComputesOverage()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio"); // 1 stanowisko → limit 200
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var month = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    // 1 wiadomość kosztująca 3 punkty (np. długa, polskie znaki) → 3 kredyty z jednej wiadomości.
    db.NotificationUsage.Add(
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 3m, month));
    await db.SaveChangesAsync(ct);

    var result = await Handler(db, tenant.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);

    Assert.Equal(1, result.SmsSent);
    Assert.Equal(3m, result.SmsPoints);
    Assert.Equal(3, result.SmsCreditsUsed);
  }

  [Fact]
  public async Task Handle_OverLimit_ComputesOverageCount_AndCost()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var allowance = App.Domain.Aggregates.TenantAggregate.Subscription.BaseSmsAllowance; // 200
    var month = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    for (var i = 0; i < allowance + 3; i++)
    {
      db.NotificationUsage.Add(
        NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 1m, month));
    }
    await db.SaveChangesAsync(ct);

    var result = await Handler(db, tenant.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);

    Assert.Equal(0, result.SmsRemaining);
    Assert.Equal(3, result.SmsOverageCount);
    Assert.Equal(3 * App.Domain.Aggregates.TenantAggregate.Subscription.OverageSmsPriceGrosze, result.OverageCostGrosze);
  }

  [Fact]
  public async Task Handle_OnlyCountsCurrentTenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantA = new Tenant("Salon A", "salon-a");
    var tenantB = new Tenant("Salon B", "salon-b");
    var month = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    var dbName = Guid.NewGuid().ToString();

    // Zapisy tenanta A — w kontekście A (guard przepuszcza).
    await using (var dbA = NewDb(dbName, tenantA.Id))
    {
      dbA.Tenants.Add(tenantA);
      dbA.NotificationUsage.AddRange(
        NotificationUsageRecord.ForSms(tenantA.Id, NotificationType.AppointmentReminderToCustomer, true, 1m, month),
        NotificationUsageRecord.ForEmail(tenantA.Id, NotificationType.NewBookingToSalon, true, month));
      await dbA.SaveChangesAsync(ct);
    }

    // Zapisy tenanta B — w kontekście B, do tej samej bazy InMemory.
    await using (var dbB = NewDb(dbName, tenantB.Id))
    {
      dbB.Tenants.Add(tenantB);
      dbB.NotificationUsage.AddRange(
        NotificationUsageRecord.ForSms(tenantB.Id, NotificationType.AppointmentReminderToCustomer, true, 5m, month),
        NotificationUsageRecord.ForSms(tenantB.Id, NotificationType.BookingConfirmationToCustomer, true, 5m, month),
        NotificationUsageRecord.ForEmail(tenantB.Id, NotificationType.CancellationToSalon, true, month));
      await dbB.SaveChangesAsync(ct);
    }

    // Raport A widzi tylko dane A (query filter), nie sumuje B.
    await using var dbReadA = NewDb(dbName, tenantA.Id);
    var resultA = await Handler(dbReadA, tenantA.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);

    Assert.Equal(1, resultA.SmsSent);
    Assert.Equal(1m, resultA.SmsPoints);
    Assert.Equal(1, resultA.EmailSent);

    // Raport B widzi tylko dane B.
    await using var dbReadB = NewDb(dbName, tenantB.Id);
    var resultB = await Handler(dbReadB, tenantB.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);

    Assert.Equal(2, resultB.SmsSent);
    Assert.Equal(10m, resultB.SmsPoints);
    Assert.Equal(1, resultB.EmailSent);
  }

  [Fact]
  public async Task Recorder_RejectsCrossTenantWrite()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantA = new Tenant("Salon A", "salon-a");
    var otherTenantId = Guid.NewGuid();
    await using var db = NewDb(tenantA.Id);
    db.Tenants.Add(tenantA);
    await db.SaveChangesAsync(ct);

    // Recorder swallow'uje wyjątek, ale guard musi zablokować zapis cudzego tenanta:
    // w kontekście A próba zapisu usage dla obcego tenanta nie może wylądować w bazie.
    var recorder = new NotificationUsageRecorder(db, NullLogger<NotificationUsageRecorder>.Instance);
    await recorder.RecordSmsAsync(otherTenantId, NotificationType.AppointmentReminderToCustomer, true, 1m, ct);

    Assert.Empty(await db.NotificationUsage.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct));
  }

  [Fact]
  public async Task Handle_ExcludesOtherMonths()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    db.NotificationUsage.AddRange(
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 1m,
        new DateTime(2026, 5, 31, 22, 0, 0, DateTimeKind.Utc)),
      NotificationUsageRecord.ForSms(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 1m,
        new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
    await db.SaveChangesAsync(ct);

    var may = await Handler(db, tenant.Id).Handle(new GetNotificationUsageQuery(2026, 5), ct);
    var june = await Handler(db, tenant.Id).Handle(new GetNotificationUsageQuery(2026, 6), ct);

    Assert.Equal(1, may.SmsSent);
    Assert.Equal(1, june.SmsSent);
  }

  [Fact]
  public async Task Recorder_PersistsRow_ForTenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio", "studio");
    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var recorder = new NotificationUsageRecorder(db, NullLogger<NotificationUsageRecorder>.Instance);
    await recorder.RecordSmsAsync(tenant.Id, NotificationType.AppointmentReminderToCustomer, true, 1.5m, ct);

    var row = Assert.Single(await db.NotificationUsage.AsNoTracking().ToListAsync(ct));
    Assert.Equal(NotificationDeliveryChannel.Sms, row.Channel);
    Assert.Equal(1.5m, row.Points);
    Assert.True(row.Success);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static GetNotificationUsageQueryHandler Handler(IApplicationDbContext db, Guid tenantId) =>
    new(db, new FakeCurrentTenantService(tenantId));

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
