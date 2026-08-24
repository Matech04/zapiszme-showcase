using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Admin włącza logowanie istniejącej pracowniczce-zasobowi (userId == null): tworzy konto Identity,
/// podpina je do istniejącego rekordu (bez duplikatu) i wysyła link „ustaw hasło".
/// </summary>
public sealed class EnableEmployeeLoginIntegrationTests
{
  [Fact]
  public async Task Admin_enables_login_for_resource_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var resourceId = CreateResourceEmployee(factory, seed.TenantId);
    var admin = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await admin.PostAsJsonAsync(
      $"/api/auth/admin/tenants/{seed.TenantId}/employees/{resourceId}/enable-login",
      new { email = "real-anna@rest-seed.local", role = "Employee" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = db.Employees.IgnoreQueryFilters().Single(e => e.Id == resourceId);
    Assert.NotNull(employee.UserId); // podpięte konto — bez duplikatu (ten sam rekord)
    Assert.Equal("real-anna@rest-seed.local", employee.Email);

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var user = await userManager.FindByIdAsync(employee.UserId!.Value.ToString());
    Assert.NotNull(user);
    Assert.Contains("Employee", await userManager.GetRolesAsync(user!));

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastEmployeeInviteUrl);
  }

  [Fact]
  public async Task Enable_login_for_employee_with_account_returns_conflict()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var admin = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    // seed.EmployeeId to właściciel — ma już konto (userId != null).
    var response = await admin.PostAsJsonAsync(
      $"/api/auth/admin/tenants/{seed.TenantId}/employees/{seed.EmployeeId}/enable-login",
      new { email = "dup@rest-seed.local", role = "Employee" },
      ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Enable_login_requires_system_admin_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var resourceId = CreateResourceEmployee(factory, seed.TenantId);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      $"/api/auth/admin/tenants/{seed.TenantId}/employees/{resourceId}/enable-login",
      new { email = "nope@rest-seed.local", role = "Employee" },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  private static Guid CreateResourceEmployee(BookingApiApplicationFactory factory, Guid tenantId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = new Employee(tenantId, userId: null, "Zasob", "Test", "resource@rest-seed.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
  }
}
