using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Faza 1 — zarządzanie kontem pracownika przez właściciela:
/// zmiana roli (awans Pracownik→Manager), zmiana e-maila, oraz ekspozycja roli/stanu zaproszenia
/// w liście pracowników. Zmiany roli/e-maila to operacje Identity (Owner-only, BusinessManagement).
/// </summary>
public sealed class EmployeeAccountManagementIntegrationTests
{
  private sealed record InviteResp(Guid UserId, Guid EmployeeId, string Role);

  private sealed record EmpRow(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Role,
    bool InvitePending);

  [Fact]
  public async Task GetEmployees_exposes_owner_role_and_not_pending_for_seeded_owner()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var rows = await client.GetFromJsonAsync<List<EmpRow>>("/api/Employees", ct);

    Assert.NotNull(rows);
    var owner = Assert.Single(rows!, r => r.Id == seed.EmployeeId);
    Assert.Equal("Owner", owner.Role);
    Assert.False(owner.InvitePending);
  }

  [Fact]
  public async Task Owner_can_promote_invited_employee_to_manager()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var employeeId = await InviteAsync(client, "promote@rest-seed.local", ct);

    var promote = await client.PutAsJsonAsync(
      $"/api/auth/employees/{employeeId}/role", new { role = "Manager" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);

    var rows = await client.GetFromJsonAsync<List<EmpRow>>("/api/Employees", ct);
    var promoted = Assert.Single(rows!, r => r.Id == employeeId);
    Assert.Equal("Manager", promoted.Role);
    // Zaproszenie wysłane, konto jeszcze nieaktywowane.
    Assert.True(promoted.InvitePending);
  }

  [Fact]
  public async Task Owner_cannot_change_own_role_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/auth/employees/{seed.EmployeeId}/role", new { role = "Manager" }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ChangeRole_to_owner_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var employeeId = await InviteAsync(client, "invalidrole@rest-seed.local", ct);

    var response = await client.PutAsJsonAsync(
      $"/api/auth/employees/{employeeId}/role", new { role = "Owner" }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Manager_cannot_change_employee_role_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var manager = factory.CreateManagerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await manager.PutAsJsonAsync(
      $"/api/auth/employees/{seed.EmployeeId}/role", new { role = "Manager" }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Owner_can_change_invited_employee_email()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var employeeId = await InviteAsync(client, "old-address@rest-seed.local", ct);

    var change = await client.PutAsJsonAsync(
      $"/api/auth/employees/{employeeId}/email", new { email = "new-address@rest-seed.local" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

    var rows = await client.GetFromJsonAsync<List<EmpRow>>("/api/Employees", ct);
    var updated = Assert.Single(rows!, r => r.Id == employeeId);
    Assert.Equal("new-address@rest-seed.local", updated.Email);
  }

  private static async Task<Guid> InviteAsync(HttpClient owner, string email, CancellationToken ct)
  {
    var response = await owner.PostAsJsonAsync(
      "/api/auth/employees",
      new { email, displayName = "Invited Person", firstName = "Invited", lastName = "Person", role = "Employee" },
      ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var payload = await response.Content.ReadFromJsonAsync<InviteResp>(ct);
    Assert.NotNull(payload);
    return payload!.EmployeeId;
  }

  private static void SetTenantToActivePlan(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.SetSubscription(App.Domain.Aggregates.TenantAggregate.Subscription.AdminReset(
      App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.Active, seats: 1, isFoundingMember: false,
      trialEndsAt: null, currentPeriodEndsAt: DateTimeOffset.UtcNow.AddYears(1)));
    db.SaveChanges();
  }
}
