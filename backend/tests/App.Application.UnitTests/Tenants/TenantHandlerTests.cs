using App.Application.Common.Interfaces;
using App.Application.SalonSettings.Commands.UpdateCurrentSalonSettings;
using App.Application.Tenants.Commands.SetBookingAccessPolicy;
using App.Application.Tenants.Commands.SetTenantSubscription;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using DomainSubscription = App.Domain.Aggregates.TenantAggregate.Subscription;

namespace App.Application.UnitTests.Tenants;

/// <summary>
/// APP-TEN — handlerowe testy Tenant: SetBookingAccessPolicy, SetTenantSubscription, UpdateCurrentSalonSettings (phone disabled).
/// </summary>
public sealed class TenantHandlerTests
{
  // TEN-006: SetBookingAccessPolicy persists policy change
  [Fact]
  public async Task SetBookingAccessPolicy_persists_invite_only()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    var handler = new SetBookingAccessPolicyCommandHandler(
      new TenantRepository(db), db, new FakeCurrentTenantService(tenant.Id));

    await handler.Handle(new SetBookingAccessPolicyCommand(BookingAccessPolicy.InviteOnly), ct);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.Equal(BookingAccessPolicy.InviteOnly, reloaded!.BookingAccessPolicy);
  }

  // TEN-010: SetTenantSubscription Active (admin override)
  [Fact]
  public async Task SetTenantSubscription_to_Active_sets_status_and_seats()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    var handler = new SetTenantSubscriptionCommandHandler(db, db);

    var period = DateTimeOffset.UtcNow.AddMonths(1);
    await handler.Handle(new SetTenantSubscriptionCommand(
      tenant.Id, SubscriptionStatus.Active, Seats: 2, IsFoundingMember: false,
      TrialEndsAt: null, CurrentPeriodEndsAt: period), ct);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.Equal(SubscriptionStatus.Active, reloaded!.Subscription.Status);
    Assert.Equal(2, reloaded.Subscription.Seats);
    Assert.NotNull(reloaded.Subscription.CurrentPeriodEndsAt);
  }

  // TEN-010: SetTenantSubscription Trial with explicit TrialEndsAt
  [Fact]
  public async Task SetTenantSubscription_to_Trial_with_explicit_end_date()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    var handler = new SetTenantSubscriptionCommandHandler(db, db);

    var endsAt = DateTimeOffset.UtcNow.AddDays(7);
    await handler.Handle(new SetTenantSubscriptionCommand(
      tenant.Id, SubscriptionStatus.Trial, Seats: 1, IsFoundingMember: false,
      TrialEndsAt: endsAt, CurrentPeriodEndsAt: null), ct);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.Equal(SubscriptionStatus.Trial, reloaded!.Subscription.Status);
    Assert.NotNull(reloaded.Subscription.TrialEndsAt);
  }

  // TEN-010: NotFound for unknown tenant id
  [Fact]
  public async Task SetTenantSubscription_throws_NotFound_for_unknown_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _) = SetupDb();
    var handler = new SetTenantSubscriptionCommandHandler(db, db);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetTenantSubscriptionCommand(
        Guid.NewGuid(), SubscriptionStatus.Trial, 1, false, null, null), ct));
  }

  // TEN-008: UpdateCurrentSalonSettings akceptuje CustomerVerificationChannel.Phone (OTP SMS dla klienta).
  [Fact]
  public async Task UpdateCurrentSalonSettings_with_phone_channel_persists_phone()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    var handler = new UpdateCurrentSalonSettingsCommandHandler(
      new FakeCurrentTenantService(tenant.Id),
      new TenantRepository(db),
      db,
      new NoOpTenantSlugCache());

    await handler.Handle(
      new UpdateCurrentSalonSettingsCommand(
        tenant.Name, tenant.Slug,
        CustomerVerificationChannel.Phone,
        15, "Europe/Warsaw", "PLN"),
      ct);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.Equal(CustomerVerificationChannel.Phone, reloaded!.CustomerVerificationChannel);
  }

  [Fact]
  public async Task UpdateCurrentSalonSettings_persists_collect_instagram_handle()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();
    var handler = new UpdateCurrentSalonSettingsCommandHandler(
      new FakeCurrentTenantService(tenant.Id),
      new TenantRepository(db),
      db,
      new NoOpTenantSlugCache());

    await handler.Handle(
      new UpdateCurrentSalonSettingsCommand(
        tenant.Name, tenant.Slug,
        CustomerVerificationChannel.Email,
        15, "Europe/Warsaw", "PLN",
        CollectInstagramHandle: true),
      ct);

    var reloaded = await db.Tenants.FindAsync(new object[] { tenant.Id }, ct);
    Assert.True(reloaded!.CollectInstagramHandle);
  }

  // TEN-014: Updating tenant timezone does not alter UTC timestamps stored on existing appointments
  [Fact]
  public async Task UpdateSettings_timezone_does_not_mutate_appointment_times()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = SetupDb();

    // Insert an appointment
    var apptStartTime = new TimeOnly(10, 0);
    var apptEndTime = new TimeOnly(10, 30);
    var apptDate = new DateOnly(2026, 8, 15);
    var appointment = new App.Domain.Aggregates.AppointmentAggregate.Appointment(
      tenant.Id, Guid.NewGuid(), Guid.NewGuid(), null,
      apptDate, apptStartTime, apptEndTime,
      App.Domain.Aggregates.AppointmentAggregate.AppointmentStatus.Booked,
      new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    // Update tenant timezone
    var handler = new UpdateCurrentSalonSettingsCommandHandler(
      new FakeCurrentTenantService(tenant.Id),
      new TenantRepository(db),
      db,
      new NoOpTenantSlugCache());
    await handler.Handle(
      new UpdateCurrentSalonSettingsCommand(
        tenant.Name, tenant.Slug,
        CustomerVerificationChannel.Email,
        15, "Europe/London", "PLN"),
      ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(apptDate, reloaded!.Date);
    Assert.Equal(apptStartTime, reloaded.StartTime);
    Assert.Equal(apptEndTime, reloaded.EndTime);
  }

  // ── helpers ─────────────────────────────────────────────────────────────────────────────

  private static (ApplicationDbContext db, Tenant tenant) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var tenant = new Tenant("Test Salon", "test-salon-" + Guid.NewGuid().ToString("N")[..6]);
    typeof(Entity).GetProperty("Id")!.SetValue(tenant, tenantId);
    db.Tenants.Add(tenant);
    db.SaveChanges();
    return (db, tenant);
  }

  /// <summary>Cache slug→tenant nie ma znaczenia dla tych testów — liczy się sam zapis ustawień.
  /// Realne unieważnienie pokrywa TenantSlugCacheInvalidationIntegrationTests (przez HTTP).</summary>
  private sealed class NoOpTenantSlugCache : ITenantSlugCache
  {
    public void Invalidate(string slug) { }
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
