using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// SVC-005/SVC-006 — POST/PUT/DELETE wymaga StaffManagement; GET 200 dla GeneralAccess.
/// SVC-007 — cross-tenant GET/PUT/DELETE zwraca 404.
/// SVC-010 — booking categories scope per tenant.
/// </summary>
public sealed class ServiceAuthAndIsolationIntegrationTests
{
  // SVC-005: POST /api/Services → 403 dla Employee (GeneralAccess only)
  [Fact]
  public async Task Employee_role_cannot_create_service_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/Services",
      new
      {
        categoryId = seed.ServiceCategoryId,
        vatRateId = seed.VatRateId,
        name = "Nowa usługa",
        amount = 100m,
        currency = "PLN",
        durationInMinutes = 30,
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // SVC-005: PUT /api/Services/{id} → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_update_service_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/Services/{seed.ServiceId}",
      new
      {
        id = seed.ServiceId,
        categoryId = seed.ServiceCategoryId,
        vatRateId = seed.VatRateId,
        name = "Updated",
        amount = 100m,
        currency = "PLN",
        durationInMinutes = 30,
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // SVC-003 Security / SVC-005: DELETE /api/Services/{id} → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_delete_service_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.DeleteAsync($"/api/Services/{seed.ServiceId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // SVC-005 HappyPath: GET /api/Services → 200 dla Employee (GeneralAccess)
  [Fact]
  public async Task Employee_role_can_get_services_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Services", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  // SVC-006: POST /api/ServiceCategories → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_create_service_category_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/ServiceCategories",
      new { name = "Nowa kategoria", orderIndex = 5 },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // SVC-006: PUT /api/ServiceCategories/{id} → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_update_service_category_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/ServiceCategories/{seed.ServiceCategoryId}",
      new { id = seed.ServiceCategoryId, name = "Renamed", orderIndex = 7 },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // SVC-006 HappyPath: GET /api/ServiceCategories → 200 dla Employee
  [Fact]
  public async Task Employee_role_can_get_service_categories_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ServiceCategories", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  // SVC-007: GET /api/Services/{id} → 404 dla service z innego tenanta
  [Fact]
  public async Task Owner_cannot_get_service_from_other_tenant_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/Services/{second.ServiceId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // SVC-007: PUT /api/Services/{id} → 404 dla service z innego tenanta
  [Fact]
  public async Task Owner_cannot_update_service_from_other_tenant_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PutAsJsonAsync(
      $"/api/Services/{second.ServiceId}",
      new
      {
        id = second.ServiceId,
        categoryId = own.ServiceCategoryId,
        vatRateId = own.VatRateId,
        name = "Hack",
        amount = 100m,
        currency = "PLN",
        durationInMinutes = 30,
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // SVC-007: DELETE /api/Services/{id} → 404 dla service z innego tenanta
  [Fact]
  public async Task Owner_cannot_delete_service_from_other_tenant_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.DeleteAsync($"/api/Services/{second.ServiceId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // SVC-007: GET /api/ServiceCategories/{id} → 404 dla kategorii z innego tenanta
  [Fact]
  public async Task Owner_cannot_get_service_category_from_other_tenant_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/ServiceCategories/{second.ServiceCategoryId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
