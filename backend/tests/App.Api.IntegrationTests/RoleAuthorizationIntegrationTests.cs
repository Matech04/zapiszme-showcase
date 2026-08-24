using System.Net;
using System.Net.Http.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>Regression tests for role-based access rules on staff/admin API endpoints.</summary>
public sealed class RoleAuthorizationIntegrationTests
{
  /// <summary>
  /// Termin wizyty LICZONY, nie zaszyty. Stała „2026-08-03" była bombą zegarową: po tej dacie
  /// `Employee_can_create_own_panel_appointment` dostawał 400 (termin przeszły) zamiast 200.
  /// Dwa pozostałe testy z tą datą przechodziły dalej, ale wyłącznie przypadkiem — ich bramka
  /// (403/404) odpala się PRZED walidacją terminu, więc maskowała ten sam problem.
  /// </summary>
  private static readonly string AppointmentDate =
    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");

  [Fact]
  public async Task Unauthenticated_user_cannot_access_staff_api()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_access_system_admin_api()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Tenants", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Admin_without_impersonation_cannot_read_staff_panel_data()
  {
    // Model support impersonation: Admin jest dopisany do polityk tenantowych, więc PRZECHODZI
    // autoryzację endpointów panelu — ale bez aktywnej sesji wsparcia nie ma TenantId, więc
    // żądanie kończy się TenantMissing (400), a NIE zwróceniem danych salonu (200). Realną
    // bramką jest rozwiązanie tenanta, nie sama rola. Pełne pokrycie w
    // SupportImpersonationIntegrationTests.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Manager_can_access_staff_management_endpoints()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateManagerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/SalonSettings", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_mutate_salon_settings()
  {
    // PUT /api/SalonSettings wymaga BusinessManagement (tylko Owner) — slug/strefa/polityka
    // widoczności to ustawienia biznesowe. GET jest dostępny dla Employee — UI kalendarza czyta
    // `StaffCalendarVisibilityPolicy` z tenant settings, żeby dostosować widoczność/akcje (F2.3).
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      "/api/SalonSettings",
      new
      {
        name = "X",
        slug = "x",
        customerVerificationChannel = 1, // Email
        appointmentSlotStepMinutes = 15,
        timeZoneId = "Europe/Warsaw",
        currency = "PLN",
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_can_read_salon_settings_for_calendar_policy()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/SalonSettings", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Employee_can_access_general_staff_endpoints()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Employee_can_create_own_panel_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = AppointmentDate,
        startTime = "10:00:00",
        customerId = seed.CustomerId,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_create_panel_appointment_for_other_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // Domyślna polityka salonu to TeamFull (tryb zaufania) — ten test bada salon zawężony,
    // więc ustawiamy OwnCalendarOnly jawnie. Bez tego pracownik przechodzi autoryzację.
    SetOwnCalendarOnly(factory.Services, seed.TenantId);
    var otherEmployeeId = AddOtherEmployee(factory.Services, seed.TenantId);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = otherEmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = AppointmentDate,
        startTime = "10:00:00",
        customerId = seed.CustomerId,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Manager_cannot_access_owner_only_business_management_endpoint()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateManagerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.DeleteAsync($"/api/Customers/{seed.CustomerId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_can_update_own_employee_profile()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
        $"/api/Employees/{seed.EmployeeId}",
        new { firstName = "Ann", lastName = "Updated" },
        ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_update_another_employee_profile()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var otherEmployeeId = AddOtherEmployee(factory.Services, seed.TenantId);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
        $"/api/Employees/{otherEmployeeId}",
        new { firstName = "Other", lastName = "Updated" },
        ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_update_employee_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PutAsJsonAsync(
      $"/api/Employees/{second.EmployeeId}",
      new { firstName = "Cross", lastName = "Tenant" },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_create_appointment_for_employee_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = second.EmployeeId,
        serviceIds = new[] { own.ServiceId },
        date = AppointmentDate,
        startTime = "10:00:00",
        customerId = own.CustomerId,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Employee_list_is_isolated_per_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var secondOwnerClient = CreateClientForUser(factory, IntegrationTestUserIds.SecondSalonOwner, "Owner");
    var ct = TestContext.Current.CancellationToken;

    var ownEmployeesResponse = await ownerClient.GetAsync("/api/Employees", ct);
    var secondEmployeesResponse = await secondOwnerClient.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.OK, ownEmployeesResponse.StatusCode);
    Assert.Equal(HttpStatusCode.OK, secondEmployeesResponse.StatusCode);

    var ownEmployees = await ownEmployeesResponse.Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);
    var secondEmployees = await secondEmployeesResponse.Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);

    Assert.NotNull(ownEmployees);
    Assert.NotNull(secondEmployees);
    Assert.Contains(ownEmployees, e => e.Id == own.EmployeeId);
    Assert.DoesNotContain(ownEmployees, e => e.Id == second.EmployeeId);
    Assert.Contains(secondEmployees, e => e.Id == second.EmployeeId);
    Assert.DoesNotContain(secondEmployees, e => e.Id == own.EmployeeId);
  }

  private static void SetOwnCalendarOnly(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.Single(t => t.Id == tenantId);
    tenant.Update(tenant.Name, tenant.Slug,
      staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    db.SaveChanges();
  }

  private static Guid AddOtherEmployee(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = new Employee(tenantId, TestIdentity.Ensure(db, Guid.NewGuid(), "other@rest-seed.local"), "Other", "Employee", "other@rest-seed.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
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
