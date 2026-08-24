using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Common.Security;

/// <summary>
/// Macierz `rola × StaffCalendarVisibilityPolicy` bez HTTP — odpowiednik siatki integracyjnej
/// `AppointmentAuthorizationMatrixIntegrationTests`, tylko szybszy i bliżej reguły.
///
/// Rozróżnienia, które łatwo zgubić przy refaktorze:
///  - ODCZYT przechodzi od `TeamReadOnly`, ZAPIS dopiero od `TeamFull`,
///  - Kiosk nie ma własnego kalendarza, więc omija politykę całkiem,
///  - `ResolveCalendarReadScope` przy `OwnCalendarOnly` ZAWĘŻA zapytanie ogólne, ale ODMAWIA
///    jawnego pytania o cudzego pracownika,
///  - `EnsureSelfOrStaffManager` (profil) nie zna polityki kalendarza — to osobna oś.
/// </summary>
public sealed class StaffAccessPolicyTests
{
  private static readonly Guid Me = Guid.NewGuid();
  private static readonly Guid Teammate = Guid.NewGuid();

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  // ── Odczyt kalendarza ────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task View_own_calendar_is_always_allowed()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());
    await ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Me, Ct);
  }

  [Theory]
  [InlineData(StaffCalendarVisibilityPolicy.TeamReadOnly)]
  [InlineData(StaffCalendarVisibilityPolicy.TeamFull)]
  public async Task Employee_may_view_teammate_calendar_from_TeamReadOnly_up(StaffCalendarVisibilityPolicy p)
  {
    var ctx = Ctx(p, Employee());
    await ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct);
  }

  [Fact]
  public async Task Employee_may_not_view_teammate_calendar_under_OwnCalendarOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct));

    Assert.Equal(ErrorCodes.CalendarCannotViewOtherEmployee, ex.ErrorCode);
  }

  // ── Zapis: TeamReadOnly NIE wystarcza ────────────────────────────────────────────────────

  [Fact]
  public async Task Employee_may_not_mutate_teammate_calendar_under_TeamReadOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamReadOnly, Employee());

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct));

    Assert.Equal(ErrorCodes.CalendarCannotMutateOtherEmployee, ex.ErrorCode);
  }

  [Fact]
  public async Task Employee_may_mutate_teammate_calendar_under_TeamFull()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, Employee());
    await ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);
  }

  // ── Role omijające politykę ──────────────────────────────────────────────────────────────

  [Theory]
  [InlineData(StaffCalendarVisibilityPolicy.OwnCalendarOnly)]
  [InlineData(StaffCalendarVisibilityPolicy.TeamReadOnly)]
  public async Task StaffManager_ignores_policy(StaffCalendarVisibilityPolicy p)
  {
    var ctx = Ctx(p, Manager());
    await ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct);
    await ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);
  }

  /// <summary>Kiosk nie ma własnego kalendarza — bez obejścia polityki nie mógłby nic.</summary>
  [Theory]
  [InlineData(StaffCalendarVisibilityPolicy.OwnCalendarOnly)]
  [InlineData(StaffCalendarVisibilityPolicy.TeamReadOnly)]
  public async Task Desk_account_ignores_policy(StaffCalendarVisibilityPolicy p)
  {
    var ctx = Ctx(p, Kiosk());
    await ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct);
    await ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);
  }

  [Fact]
  public async Task Caller_without_linked_employee_is_forbidden()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, new FakeUser(callerEmployeeId: null));

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct));

    Assert.Equal(ErrorCodes.EmployeeNoLinkedAccount, ex.ErrorCode);
  }

  // ── Zakres odczytu listy ─────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Read_scope_narrows_open_query_to_self_under_OwnCalendarOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());

    Assert.Equal(Me, await ctx.Policy.ResolveCalendarReadScopeAsync(null, Ct));
  }

  [Fact]
  public async Task Read_scope_forbids_explicit_request_for_teammate_under_OwnCalendarOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.ResolveCalendarReadScopeAsync(Teammate, Ct));
  }

  [Fact]
  public async Task Read_scope_passes_through_for_TeamReadOnly_and_for_managers()
  {
    var employee = Ctx(StaffCalendarVisibilityPolicy.TeamReadOnly, Employee());
    Assert.Equal(Teammate, await employee.Policy.ResolveCalendarReadScopeAsync(Teammate, Ct));
    Assert.Null(await employee.Policy.ResolveCalendarReadScopeAsync(null, Ct));

    var manager = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Manager());
    Assert.Null(await manager.Policy.ResolveCalendarReadScopeAsync(null, Ct));
  }

  // ── Wizyta: 404 to nie 403 ───────────────────────────────────────────────────────────────

  [Fact]
  public async Task Mutating_missing_appointment_is_NotFound_not_Forbidden()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());

    await Assert.ThrowsAsync<NotFoundException>(() =>
      ctx.Policy.EnsureCanMutateAppointmentAsync(Guid.NewGuid(), Ct));
  }

  [Fact]
  public async Task Mutating_teammates_appointment_is_forbidden_under_TeamReadOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamReadOnly, Employee());
    var appointmentId = ctx.SeedAppointmentFor(Teammate);

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanMutateAppointmentAsync(appointmentId, Ct));
  }

  [Fact]
  public async Task Mutating_teammates_appointment_is_allowed_under_TeamFull()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, Employee());
    var appointmentId = ctx.SeedAppointmentFor(Teammate);

    await ctx.Policy.EnsureCanMutateAppointmentAsync(appointmentId, Ct);
  }

  [Fact]
  public async Task Mutating_own_appointment_is_allowed_under_OwnCalendarOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());
    var appointmentId = ctx.SeedAppointmentFor(Me);

    await ctx.Policy.EnsureCanMutateAppointmentAsync(appointmentId, Ct);
  }

  // ── Memoizacja polityki ──────────────────────────────────────────────────────────────────

  /// <summary>
  /// Dowód, że polityka jest czytana raz na żądanie: po pierwszym sprawdzeniu podmieniamy ją w
  /// bazie, a serwis nadal używa zapamiętanej. Gdyby czytał za każdym razem, drugie wywołanie
  /// odmówiłoby dostępu.
  /// </summary>
  [Fact]
  public async Task Policy_is_read_once_per_request()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, Employee());

    await ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);
    ctx.ChangePolicyInDatabase(StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    await ctx.Policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);
  }

  /// <summary>
  /// Cache trzyma parę `(tenantId, policy)`. Gdyby instancja kiedykolwiek zobaczyła drugi tenant
  /// (nie powinna — scope = żądanie), przeliczy politykę zamiast wydać cudzą.
  /// </summary>
  [Fact]
  public async Task Cached_policy_does_not_leak_across_tenants()
  {
    var store = Guid.NewGuid().ToString();
    using var seedDb = NewDb(store, new MutableTenantService(null));
    var trusting = AddTenant(seedDb, StaffCalendarVisibilityPolicy.TeamFull);
    var strict = AddTenant(seedDb, StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    seedDb.SaveChanges();

    var tenant = new MutableTenantService(trusting);
    var policy = new StaffAccessPolicy(NewDb(store, tenant), tenant, Employee());

    await policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct);

    tenant.Set(strict);
    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      policy.EnsureCanMutateEmployeeCalendarAsync(Teammate, Ct));
  }

  [Fact]
  public async Task Missing_tenant_throws_instead_of_silently_allowing()
  {
    var policy = new StaffAccessPolicy(
      NewDb(Guid.NewGuid().ToString(), new MutableTenantService(null)),
      new MutableTenantService(null),
      Employee());

    await Assert.ThrowsAsync<NoTenantHeader>(() =>
      policy.EnsureCanViewEmployeeCalendarAsync(Teammate, Ct));
  }

  // ── Profil pracownika: oś niezależna od kalendarza ───────────────────────────────────────

  [Fact]
  public void EnsureSelfOrStaffManager_is_independent_of_calendar_policy()
  {
    // TeamFull pozwala pracownikowi na cudzy KALENDARZ, ale nie na cudzy PROFIL.
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, Employee());

    ctx.Policy.EnsureSelfOrStaffManager(Me);

    var ex = Assert.Throws<ForbiddenAccessException>(() => ctx.Policy.EnsureSelfOrStaffManager(Teammate));
    Assert.Equal(ErrorCodes.EmployeeCannotMutateOtherProfile, ex.ErrorCode);
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────────────

  private static FakeUser Employee() => new(callerEmployeeId: Me);
  private static FakeUser Manager() => new(callerEmployeeId: Me, canManageOthers: true);
  private static FakeUser Kiosk() => new(callerEmployeeId: Me, isDesk: true);

  private static TestContextBag Ctx(StaffCalendarVisibilityPolicy policy, FakeUser user)
  {
    var store = Guid.NewGuid().ToString();

    // Seed przez kontekst BEZ ambient-tenanta (guard w SaveChanges odpala się tylko gdy ustawiony).
    using var seedDb = NewDb(store, new MutableTenantService(null));
    var tenantId = AddTenant(seedDb, policy);
    seedDb.SaveChanges();

    // Kontekst serwisu MUSI znać tenanta — inaczej globalne query filtry ukryją mu wizyty
    // i `EnsureCanMutateAppointmentAsync` zwróci NotFound zamiast rozstrzygnąć uprawnienia.
    var tenantService = new MutableTenantService(tenantId);
    var db = NewDb(store, tenantService);

    return new TestContextBag(store, tenantId, new StaffAccessPolicy(db, tenantService, user));
  }

  private sealed record TestContextBag(string Store, Guid TenantId, StaffAccessPolicy Policy)
  {
    public Guid SeedAppointmentFor(Guid employeeId)
    {
      using var db = NewDb(Store, new MutableTenantService(null));
      var appointment = new Appointment(
        TenantId, employeeId, Guid.NewGuid(), customerId: null,
        new DateOnly(2026, 11, 10), new TimeOnly(9, 0), new TimeOnly(9, 30),
        AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
      db.Appointments.Add(appointment);
      db.SaveChanges();
      return appointment.Id;
    }

    /// <summary>Agregat sam generuje `Id`, więc zwracamy je — nie da się go narzucić z zewnątrz.</summary>
    public Guid SeedEmployee()
    {
      using var db = NewDb(Store, new MutableTenantService(null));
      var e = new App.Domain.Aggregates.EmployeeAggregate.Employee(
        TenantId, userId: null, "Kolega", "Zespołowy", $"{Guid.NewGuid():N}@teammate.local");
      db.Employees.Add(e);
      db.SaveChanges();
      return e.Id;
    }

    public void ChangePolicyInDatabase(StaffCalendarVisibilityPolicy policy)
    {
      using var db = NewDb(Store, new MutableTenantService(null));
      var tenant = db.Tenants.IgnoreQueryFilters().Single(t => t.Id == TenantId);
      tenant.Update(tenant.Name, tenant.Slug, staffCalendarVisibilityPolicy: policy);
      db.SaveChanges();
    }
  }

  private static Guid AddTenant(ApplicationDbContext db, StaffCalendarVisibilityPolicy policy)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var tenant = new Tenant($"Salon {suffix}", $"salon-{suffix}");
    tenant.Update(tenant.Name, tenant.Slug, staffCalendarVisibilityPolicy: policy);
    db.Tenants.Add(tenant);
    return tenant.Id;
  }

  private static ApplicationDbContext NewDb(string store, ICurrentTenantService tenant) =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(store).Options, tenant);

  private sealed class MutableTenantService : ICurrentTenantService
  {
    private Guid? _tenantId;
    public MutableTenantService(Guid? tenantId) => _tenantId = tenantId;
    public Guid? TenantId => _tenantId;
    public void Set(Guid? tenantId) => _tenantId = tenantId;
  }

  private sealed class FakeUser : ICurrentUserAccessor
  {
    public FakeUser(Guid? callerEmployeeId, bool canManageOthers = false, bool isDesk = false)
    {
      CallerEmployeeId = callerEmployeeId;
      CanManageOtherEmployees = canManageOthers;
      IsDeskAccount = isDesk;
    }

    public Guid? UserId => Guid.Empty;
    public Guid? CallerEmployeeId { get; }
    public bool CanManageOtherEmployees { get; }
    public bool IsSystemAdmin => false;
    public bool IsDeskAccount { get; }
  }

  // ── Odczyt danych kalendarza kolegi (grafik, urlopy, usługi) ─────────────────────────────

  [Fact]
  public async Task Employee_may_read_teammate_calendar_data_under_TeamReadOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamReadOnly, Employee());
    var teammateId = ctx.SeedEmployee();

    await ctx.Policy.EnsureCanReadEmployeeCalendarDataAsync(teammateId, Ct);
  }

  [Fact]
  public async Task Employee_may_not_read_teammate_calendar_data_under_OwnCalendarOnly()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());
    var teammateId = ctx.SeedEmployee();

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanReadEmployeeCalendarDataAsync(teammateId, Ct));
  }

  /// <summary>Obcy tenant to 403, a nie 404 z zapytania — nawet w trybie pełnego zaufania.</summary>
  [Fact]
  public async Task Employee_may_not_read_calendar_data_of_other_tenants_employee()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.TeamFull, Employee());
    // Pracownik spoza tego salonu — brak rekordu w tenantcie.

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      ctx.Policy.EnsureCanReadEmployeeCalendarDataAsync(Guid.NewGuid(), Ct));
  }

  [Fact]
  public async Task Own_calendar_data_needs_no_membership_lookup()
  {
    var ctx = Ctx(StaffCalendarVisibilityPolicy.OwnCalendarOnly, Employee());

    await ctx.Policy.EnsureCanReadEmployeeCalendarDataAsync(Me, Ct);
  }
}
