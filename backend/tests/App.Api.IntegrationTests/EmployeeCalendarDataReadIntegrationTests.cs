using System.Net;
using App.Domain.Aggregates.EmployeeAggregate;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// ODCZYT danych kalendarza kolegi (godziny pracy, dni specjalne, urlopy, przypisane usługi) jest
/// szerszy niż zapis: pracownik w salonie z widocznością kalendarza zespołu
/// (`StaffCalendarVisibilityPolicy` ≥ TeamReadOnly) musi je widzieć, inaczej kalendarz pokazuje
/// „Dzień wolny", pozwala umówić wizytę na czyjś urlop, a formularz nowej wizyty twierdzi, że
/// kolega nie ma żadnych usług. ZAPIS pozostaje self-or-manager.
/// </summary>
public sealed class EmployeeCalendarDataReadIntegrationTests
{
  public static TheoryData<string> ReadEndpoints() =>
    new() { "employee-schedules", "schedule-overrides", "leaves", "services" };

  [Theory]
  [MemberData(nameof(ReadEndpoints))]
  public async Task Employee_can_read_teammate_calendar_data_under_TeamFull(string endpoint)
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // Default nowego tenanta to TeamFull, ale ustawiamy jawnie — test nie ma zależeć od defaultu.
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Employees/{teammateId}/{endpoint}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Theory]
  [MemberData(nameof(ReadEndpoints))]
  public async Task Employee_cannot_read_teammate_calendar_data_under_OwnCalendarOnly(string endpoint)
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Employees/{teammateId}/{endpoint}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  /// <summary>
  /// `AbsenceType.SickLeave` to dana o zdrowiu (art. 9 RODO). Kolega z zespołu i terminal Recepcji
  /// mogą wiedzieć, ŻE ktoś jest niedostępny, ale nie DLACZEGO. Zakres dat, status wniosku i
  /// `blocksDay` zostają — bez nich kalendarz zespołu pozwoliłby umówić wizytę na czyjeś L4.
  /// </summary>
  [Theory]
  [InlineData("employee")]
  [InlineData("kiosk")]
  public async Task Teammate_and_desk_see_leave_dates_but_not_the_reason(string caller)
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);
    SeedSickLeave(factory.Services, teammateId);

    var client = caller == "kiosk" ? factory.CreateKioskClient() : factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var leaves = await client.GetFromJsonAsync<List<LeaveProbe>>(
      $"/api/Employees/{teammateId}/leaves", ct);

    var leave = Assert.Single(leaves!);
    Assert.Null(leave.AbsenceType);                            // powód zamaskowany
    Assert.Equal(TestDates.InDays(60), leave.StartDate);   // zakres dat zachowany
    Assert.Equal(TestDates.InDays(64), leave.EndDate);
    Assert.True(leave.BlocksDay);                              // wciąż wiadomo, że dzień jest zajęty
  }

  /// <summary>
  /// Regresja, którą łatwo wprowadzić „przy okazji" maskowania: gdyby razem z typem zniknęła
  /// informacja o blokadzie dnia, front (`findBlockingLeaveForDate`) przestałby uznawać
  /// nieobecność kolegi za dzień wolny i pozwolił umówić klientkę na jego urlop.
  /// </summary>
  [Fact]
  public async Task Masked_special_day_does_not_block_the_day()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);
    SeedLeave(factory.Services, teammateId, AbsenceType.SpecialDay);

    var ct = TestContext.Current.CancellationToken;
    var leaves = await factory.CreateEmployeeClient()
      .GetFromJsonAsync<List<LeaveProbe>>($"/api/Employees/{teammateId}/leaves", ct);

    var leave = Assert.Single(leaves!);
    Assert.Null(leave.AbsenceType);
    Assert.False(leave.BlocksDay);
  }

  [Fact]
  public async Task Owner_still_sees_the_reason_of_absence()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);
    SeedSickLeave(factory.Services, teammateId);

    var ct = TestContext.Current.CancellationToken;
    var leaves = await factory.CreateOwnerClient()
      .GetFromJsonAsync<List<LeaveProbe>>($"/api/Employees/{teammateId}/leaves", ct);

    var leave = Assert.Single(leaves!);
    Assert.Equal((int)AbsenceType.SickLeave, leave.AbsenceType);
  }

  [Fact]
  public async Task Employee_sees_the_reason_of_own_absence()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    SeedSickLeave(factory.Services, seed.EmployeeId);

    var ct = TestContext.Current.CancellationToken;
    var leaves = await factory.CreateEmployeeClient()
      .GetFromJsonAsync<List<LeaveProbe>>($"/api/Employees/{seed.EmployeeId}/leaves", ct);

    var leave = Assert.Single(leaves!);
    Assert.Equal((int)AbsenceType.SickLeave, leave.AbsenceType);
  }

  /// <summary>
  /// Celowo NIE deserializujemy do `EmployeeLeaveDto` — sonda po surowym JSON-ie sprawdza kontrakt
  /// wystawiony na zewnątrz, a nie to, czy typ w C# da się wypełnić.
  /// </summary>
  private sealed record LeaveProbe(
    Guid Id, DateOnly StartDate, DateOnly EndDate, int? AbsenceType, int AbsenceStatus, bool BlocksDay);

  private static void SeedSickLeave(IServiceProvider rootServices, Guid employeeId) =>
    SeedLeave(rootServices, employeeId, AbsenceType.SickLeave);

  private static void SeedLeave(IServiceProvider rootServices, Guid employeeId, AbsenceType type)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = db.Employees.IgnoreQueryFilters().Include(e => e.Leaves).Single(e => e.Id == employeeId);
    employee.AddLeave(TestDates.InDays(60), TestDates.InDays(64), type);
    db.SaveChanges();
  }

  [Fact]
  public async Task Employee_still_cannot_WRITE_teammate_schedule_under_TeamFull()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.DeleteAsync(
      $"/api/Employees/{teammateId}/schedule-overrides/2026-11-05", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_still_cannot_unassign_teammate_service_under_TeamFull()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);
    var teammateId = SeedSecondEmployee(factory.Services, seed.TenantId);

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.DeleteAsync(
      $"/api/Employees/{teammateId}/services/{seed.ServiceId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_read_calendar_data_of_other_tenant_even_under_TeamFull()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var other = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    SetPolicy(factory.Services, seed.TenantId, StaffCalendarVisibilityPolicy.TeamFull);

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync(
      $"/api/Employees/{other.EmployeeId}/employee-schedules", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  private static void SetPolicy(
    IServiceProvider rootServices, Guid tenantId, StaffCalendarVisibilityPolicy policy)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.Single(t => t.Id == tenantId);
    tenant.Update(tenant.Name, tenant.Slug, staffCalendarVisibilityPolicy: policy);
    db.SaveChanges();
  }

  private static Guid SeedSecondEmployee(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = new App.Domain.Aggregates.EmployeeAggregate.Employee(
      tenantId, userId: null, "Kolega", "Zespołowy", "teammate@schedule-read.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
  }
}
