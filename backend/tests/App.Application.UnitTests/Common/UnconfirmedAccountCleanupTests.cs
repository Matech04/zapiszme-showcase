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
/// AUTH-CLEANUP-001..004 — cykl czyszczenia niepotwierdzonych rejestracji.
///
/// Wzywamy bezpośrednio statyczny entry-point `UnconfirmedAccountCleanupHostedService.RunCycleAsync`,
/// żeby uniknąć budowania całego hostowania w testach.
/// </summary>
public sealed class UnconfirmedAccountCleanupTests
{
  private static readonly TimeSpan Grace = TimeSpan.FromHours(48);

  [Fact]
  public async Task Stale_unconfirmed_user_is_removed_with_tenant_employee_and_vat_rates()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var (userId, tenantId) = SeedRegistration(db, "stale@e.local", createdAt: utcNow - TimeSpan.FromHours(72), confirmed: false);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId, ct));
    Assert.False(await db.VatRates.IgnoreQueryFilters().AnyAsync(v => v.TenantId == tenantId, ct));
  }

  [Fact]
  public async Task Fresh_unconfirmed_user_within_grace_window_is_kept()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var (userId, tenantId) = SeedRegistration(db, "fresh@e.local", createdAt: utcNow - TimeSpan.FromHours(24), confirmed: false);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
  }

  [Fact]
  public async Task Confirmed_user_is_never_deleted_even_if_ancient()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var (userId, tenantId) = SeedRegistration(db, "old-but-confirmed@e.local", createdAt: utcNow - TimeSpan.FromDays(365), confirmed: true);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
  }

  [Fact]
  public async Task Cleanup_isolates_tenants_other_users_are_untouched()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var (staleUser, staleTenant) = SeedRegistration(db, "stale@e.local", createdAt: utcNow - TimeSpan.FromDays(7), confirmed: false);
    var (keepUser, keepTenant) = SeedRegistration(db, "keep@e.local", createdAt: utcNow - TimeSpan.FromHours(1), confirmed: false);
    var (confirmedUser, confirmedTenant) = SeedRegistration(db, "confirmed@e.local", createdAt: utcNow - TimeSpan.FromDays(7), confirmed: true);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == staleUser, ct));
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == staleTenant, ct));

    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == keepUser, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == keepTenant, ct));

    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == confirmedUser, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == confirmedTenant, ct));
  }

  [Fact]
  public async Task Unaccepted_employee_invite_must_not_delete_the_owner_salon()
  {
    // Błąd #2: niepotwierdzone zaproszenie pracownika (e-mail niepotwierdzony, brak telefonu)
    // wskazuje przez Employee na tenant WŁAŚCICIELA. Cleanup usuwa porzucone konto zaproszenia,
    // ale salon właściciela musi zostać nietknięty.
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var (ownerId, tenantId) = SeedRegistration(db, "owner@e.local", createdAt: utcNow - TimeSpan.FromDays(365), confirmed: true);
    var inviteeId = SeedStaffAccount(db, "invitee@e.local", tenantId,
      createdAt: utcNow - TimeSpan.FromHours(72), emailConfirmed: false, phoneNumber: null, phoneConfirmed: false);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == ownerId, ct));
    Assert.True(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.UserId == ownerId, ct));
    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == inviteeId, ct));
  }

  [Fact]
  public async Task Admin_created_salon_owner_without_phone_is_not_selected()
  {
    // Błąd #1: admin/create-salon daje EmailConfirmed=true, ale telefonu nie zbiera nigdy
    // (PhoneNumber=null, PhoneNumberConfirmed=false). Takie konto NIE jest porzuconą rejestracją
    // i nie może być kwalifikowane do skasowania.
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var tenant = new Tenant("Salon adminowy", "salon-adminowy");
    var user = NewUser("admin-owner@e.local", emailConfirmed: true, phoneNumber: null, phoneConfirmed: false);
    SetCreatedAt(user, utcNow - TimeSpan.FromDays(30));
    var employee = new Employee(tenant.Id, user.Id, "First", "Last", "admin-owner@e.local");
    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    await db.SaveChangesAsync(ct);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant.Id, ct));
  }

  [Fact]
  public async Task Self_signup_abandoned_at_otp_is_still_removed()
  {
    // Regresja na legalny przypadek: self-signup z podanym numerem, e-mail potwierdzony, ale
    // kod SMS nigdy nieweryfikowany. To NADAL porzucona rejestracja — pusta skorupa do usunięcia.
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var utcNow = new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    var user = NewUser("otp-abandoned@e.local", emailConfirmed: true, phoneNumber: "+48500100200", phoneConfirmed: false);
    SetCreatedAt(user, utcNow - TimeSpan.FromHours(72));
    var tenant = new Tenant("Salon otp", "salon-otp");
    var employee = new Employee(tenant.Id, user.Id, "First", "Last", "otp-abandoned@e.local");
    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    new TenantVatRateSeeder(db, new VatRateCatalog()).SeedDefaults(tenant.Id, "PL");
    await db.SaveChangesAsync(ct);

    await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, utcNow, Grace, ct, NullLogger.Instance);

    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id, ct));
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant.Id, ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static User NewUser(string email, bool emailConfirmed, string? phoneNumber, bool phoneConfirmed) =>
    new(email, email)
    {
      EmailConfirmed = emailConfirmed,
      PhoneNumber = phoneNumber,
      PhoneNumberConfirmed = phoneConfirmed,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };

  private static Guid SeedStaffAccount(
    ApplicationDbContext db,
    string email,
    Guid tenantId,
    DateTime createdAt,
    bool emailConfirmed,
    string? phoneNumber,
    bool phoneConfirmed)
  {
    var user = NewUser(email, emailConfirmed, phoneNumber, phoneConfirmed);
    SetCreatedAt(user, createdAt);
    db.Users.Add(user);
    db.Employees.Add(new Employee(tenantId, user.Id, "Staff", "Member", email));
    db.SaveChanges();
    return user.Id;
  }

  private static (Guid userId, Guid tenantId) SeedRegistration(
    ApplicationDbContext db,
    string email,
    DateTime createdAt,
    bool confirmed)
  {
    var user = new User(email, email)
    {
      EmailConfirmed = confirmed,
      // Phone gate dodany razem ze SMS OTP — "confirmed" oznacza tu pełne potwierdzenie
      // (email + telefon), nieukończona rejestracja ma oba false.
      PhoneNumberConfirmed = confirmed,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    SetCreatedAt(user, createdAt);

    var tenant = new Tenant($"Salon {email}", email.Replace("@", "-").Replace(".", "-"));
    var employee = new Employee(tenant.Id, user.Id, "First", "Last", email);
    var catalog = new VatRateCatalog();
    var seeder = new TenantVatRateSeeder(db, catalog);

    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    seeder.SeedDefaults(tenant.Id, "PL");
    db.SaveChanges();
    return (user.Id, tenant.Id);
  }

  // CreatedAt jest private set — w testach ustawiamy przez reflection, żeby symulować
  // konkretną datę rejestracji (UTC). W produkcji setter idzie tylko przez konstruktor User.
  private static void SetCreatedAt(User user, DateTime createdAt)
  {
    var prop = typeof(User).GetProperty(nameof(User.CreatedAt));
    prop!.SetValue(user, createdAt);
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
