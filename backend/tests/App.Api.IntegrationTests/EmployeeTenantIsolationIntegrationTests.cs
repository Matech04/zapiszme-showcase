using System.Net;
using System.Net.Http.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Hardening tests for strict tenant isolation on employee endpoints.
/// </summary>
public sealed class EmployeeTenantIsolationIntegrationTests
{
  [Fact]
  public async Task Owner_cannot_get_employee_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/Employees/{second.EmployeeId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_read_other_tenant_employee_services()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/Employees/{second.EmployeeId}/services", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_mutate_other_tenant_employee_leave_or_override()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var addLeave = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{second.EmployeeId}/leaves",
      new
      {
        startDate = TestDates.IsoInDays(30),
        endDate = TestDates.IsoInDays(31),
      },
      ct);

    var setOverride = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{second.EmployeeId}/schedule-overrides",
      new
      {
        date = TestDates.IsoInDays(30),
        workRanges = new[]
        {
          new { startTime = "09:00:00", endTime = "12:00:00" },
        },
        breaks = Array.Empty<object>(),
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, addLeave.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, setOverride.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_access_other_employee_scope_even_if_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var ownServices = await employeeClient.GetAsync($"/api/Employees/{own.EmployeeId}/services", ct);
    var otherServices = await employeeClient.GetAsync($"/api/Employees/{second.EmployeeId}/services", ct);
    var otherLeaves = await employeeClient.GetAsync($"/api/Employees/{second.EmployeeId}/leaves", ct);

    Assert.Equal(HttpStatusCode.OK, ownServices.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, otherServices.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, otherLeaves.StatusCode);
  }

  [Fact]
  public async Task Second_tenant_owner_sees_only_second_tenant_employees()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var secondOwnerClient = CreateClientForUser(factory, IntegrationTestUserIds.SecondSalonOwner, "Owner");
    var ct = TestContext.Current.CancellationToken;

    var response = await secondOwnerClient.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);
    Assert.NotNull(list);
    Assert.Contains(list, e => e.Id == second.EmployeeId);
    Assert.DoesNotContain(list, e => e.Id == own.EmployeeId);
  }

  private static HttpClient CreateClientForUser(
    BookingApiApplicationFactory factory,
    Guid userId,
    string role)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.UserId, userId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, role);
    return client;
  }

  private sealed record EmployeeListItem(Guid Id);
}
