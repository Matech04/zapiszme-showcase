using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>Panel salonu: <c>/api/*</c> z nagłówkami testowego uwierzytelniania (user id + role).</summary>
public sealed class StaffPanelApiIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Get_employees_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Employees", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<EmployeeListItem>>(JsonRead, ct);
    Assert.NotNull(list);
    Assert.NotEmpty(list);
  }

  [Fact]
  public async Task Get_customers_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Customers", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Post_customer_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
        "/api/Customers",
        new
        {
            firstName = "Zofia",
            lastName = "Seed",
            email = "zofia@rest-seed.local",
            phoneNumber = "+48503330003",
            generalNotes = "",
        },
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var id = await response.Content.ReadFromJsonAsync<Guid>(ct);
    Assert.NotEqual(Guid.Empty, id);
  }

  [Fact]
  public async Task Get_services_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Services", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_service_categories_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ServiceCategories", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_vat_rates_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/VatRates", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_salon_settings_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/SalonSettings", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_appointments_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/Appointments?startDate={TestDates.IsoInDays(0)}&endDate={TestDates.IsoInDays(30)}",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_customers_search_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Customers/search?searchTerm=Jan", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Salon_settings_slug_availability_treats_own_slug_as_available()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/SalonSettings/slug-availability?slug={Uri.EscapeDataString(seed.TenantSlug)}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(json.GetProperty("available").GetBoolean());
  }

  [Fact]
  public async Task Salon_settings_slug_availability_detects_other_tenant_slug()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/SalonSettings/slug-availability?slug={Uri.EscapeDataString(RestApiIntegrationSeed.SecondTenantSlug)}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.False(json.GetProperty("available").GetBoolean());
  }

  [Fact]
  public async Task Salon_settings_slug_availability_returns_unauthorized_when_anonymous()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/SalonSettings/slug-availability?slug={Uri.EscapeDataString(RestApiIntegrationSeed.TenantSlug)}",
      ct);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Salon_settings_slug_availability_returns_bad_request_for_invalid_slug()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      "/api/SalonSettings/slug-availability?slug=" + Uri.EscapeDataString("bad slug"),
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private sealed record EmployeeListItem(Guid Id, Guid? UserId, string FirstName, string LastName, string Email);
}
