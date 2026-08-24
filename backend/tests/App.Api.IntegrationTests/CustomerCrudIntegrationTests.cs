using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// CUST-001 — POST /api/Customers happy path.
/// CUST-003 — walidacja numeru telefonu na warstwszytkie HTTP.
/// CUST-004 — autoryzacja DELETE tylko dla BusinessManagement.
/// CUST-008 — whitelist/bulk-whitelist przez API.
/// CUST-009 — izolacja wyników wyszukiwania per tenant.
/// CUST-010 — izolacja GET /api/Customers + GET /{id} per tenant.
/// </summary>
public sealed class CustomerCrudIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  // CUST-001: POST /api/Customers happy path → 200 with Guid
  [Fact]
  public async Task Create_customer_returns_ok_with_guid()
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
        firstName = "Anna",
        lastName = "Nowa",
        email = "anna@example.com",
        phoneNumber = "+48501234567",
        generalNotes = "VIP",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var id = await response.Content.ReadFromJsonAsync<Guid>(JsonOpts, ct);
    Assert.NotEqual(Guid.Empty, id);
  }

  // CUST-003: POST /api/Customers with invalid phone → 400
  [Fact]
  public async Task Create_customer_with_invalid_phone_returns_bad_request()
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
        firstName = "Jan",
        lastName = "Kowalski",
        email = "jan@example.com",
        phoneNumber = "not-a-phone",
        generalNotes = "",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  // CUST-004 Auth: Employee role (GeneralAccess only) → cannot DELETE → 403
  [Fact]
  public async Task Employee_cannot_delete_customer()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.DeleteAsync($"/api/Customers/{seed.CustomerId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // CUST-008 API: PUT /api/Customers/{id}/whitelist → 204 NoContent
  [Fact]
  public async Task Set_whitelist_true_returns_no_content()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/Customers/{seed.CustomerId}/whitelist",
      new { isWhitelisted = true },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  // CUST-008 API: PUT /api/Customers/{id}/whitelist → 204 for unwhitelist
  [Fact]
  public async Task Set_whitelist_false_returns_no_content()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/Customers/{seed.CustomerId}/whitelist",
      new { isWhitelisted = false },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  // CUST-008 API: POST /api/Customers/whitelist/bulk → 200 with UpdatedCount
  [Fact]
  public async Task Bulk_whitelist_returns_ok_with_updated_count()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/Customers/whitelist/bulk",
      new
      {
        customerIds = new[] { seed.CustomerId },
        isWhitelisted = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<BulkWhitelistResultDto>(JsonOpts, ct);
    Assert.NotNull(result);
    Assert.Equal(1, result.UpdatedCount);
  }

  // CUST-009 API: search cross-tenant isolation — own tenant customer is returned, other tenant's is not
  [Fact]
  public async Task Search_returns_only_own_tenant_customers()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);

    var ownerClient = factory.CreateOwnerClient();
    var secondOwnerClient = CreateSecondOwnerClient(factory);
    var ct = TestContext.Current.CancellationToken;

    // Both tenants have "Jan Kowalski" as seeded customer — search for "Jan"
    var ownResponse = await ownerClient.GetAsync("/api/Customers/search?searchTerm=Jan", ct);
    var secondResponse = await secondOwnerClient.GetAsync("/api/Customers/search?searchTerm=Jan", ct);

    Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
    Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

    var ownResults = await ownResponse.Content
      .ReadFromJsonAsync<List<SearchResultDto>>(JsonOpts, ct);
    var secondResults = await secondResponse.Content
      .ReadFromJsonAsync<List<SearchResultDto>>(JsonOpts, ct);

    Assert.NotNull(ownResults);
    Assert.NotNull(secondResults);

    Assert.Contains(ownResults, r => r.Id == own.CustomerId);
    Assert.DoesNotContain(ownResults, r => r.Id == second.CustomerId);

    Assert.Contains(secondResults, r => r.Id == second.CustomerId);
    Assert.DoesNotContain(secondResults, r => r.Id == own.CustomerId);
  }

  // CUST-010 Integration: GET /api/Customers returns only own tenant's customers
  [Fact]
  public async Task Get_customers_returns_only_own_tenant_customers()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Customers", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<CustomerListItemDto>>(JsonOpts, ct);
    Assert.NotNull(list);
    Assert.Contains(list, c => c.Id == own.CustomerId);
    Assert.DoesNotContain(list, c => c.Id == second.CustomerId);
  }

  // CUST-010 Integration: GET /api/Customers/{id} returns 404 for unknown ID
  [Fact]
  public async Task Get_customer_by_unknown_id_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Customers/{Guid.NewGuid()}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static HttpClient CreateSecondOwnerClient(BookingApiApplicationFactory factory)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId,
      IntegrationTestUserIds.SecondSalonOwner.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    return client;
  }

  private sealed record BulkWhitelistResultDto(int UpdatedCount);
  private sealed record SearchResultDto(Guid Id);
  private sealed record CustomerListItemDto(Guid Id);
}
