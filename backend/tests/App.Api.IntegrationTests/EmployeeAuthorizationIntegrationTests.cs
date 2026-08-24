using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// EMP-003 — autoryzacja CRUD pracownika (POST/DELETE wymaga StaffManagement).
/// EMP-010 — izolacja cross-tenant dla schedule.
/// EMP-011 — Employee role Forbidden na endpointach cudzego pracownika (schedule/override).
/// </summary>
public sealed class EmployeeAuthorizationIntegrationTests
{
  // EMP-003 Security: POST /api/Employees → 403 dla Employee role (GeneralAccess only)
  [Fact]
  public async Task Employee_role_cannot_create_employee_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/Employees",
      new
      {
        firstName = "New",
        lastName = "Worker",
        email = "new@worker.local",
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // EMP-003 Security: DELETE /api/Employees/{id} → 403 dla Employee role
  [Fact]
  public async Task Employee_role_cannot_delete_employee_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.DeleteAsync($"/api/Employees/{seed.EmployeeId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // EMP-010 Security: owner cannot POST schedule for employee from another tenant → 404
  [Fact]
  public async Task Owner_cannot_set_schedule_for_other_tenant_employee_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{second.EmployeeId}/employee-schedules",
      new
      {
        activeFrom = TestDates.IsoInDays(0),
        activeTo = TestDates.IsoInDays(120),
        numberOfCycles = 1,
        days = new[]
        {
          new
          {
            cycleIndex = 1,
            workRanges = new[] { new { startTime = "09:00:00", endTime = "17:00:00" } },
            breaks = Array.Empty<object>(),
          },
        },
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // EMP-011 Security: Employee role Forbidden on other employee's schedule
  [Fact]
  public async Task Employee_cannot_view_other_employee_schedules_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync(
      $"/api/Employees/{second.EmployeeId}/employee-schedules", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // EMP-011 Security: Employee role Forbidden on schedule-overrides for other employee
  [Fact]
  public async Task Employee_cannot_set_schedule_override_for_other_employee_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.PostAsJsonAsync(
      $"/api/Employees/{second.EmployeeId}/schedule-overrides",
      new
      {
        date = TestDates.IsoInDays(15),
        workRanges = new[] { new { startTime = "09:00:00", endTime = "12:00:00" } },
        breaks = Array.Empty<object>(),
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }
}
