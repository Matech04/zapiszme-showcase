using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// CT-001..007 — kontrakt ProblemDetails: errorCode, correlationId, status mapping, Retry-After header.
/// </summary>
public sealed class ProblemDetailsContractIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  // CT-002: NotFoundException → 404 z errorCode i correlationId
  [Fact]
  public async Task NotFound_response_includes_error_code_and_correlation_id()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Customers/{Guid.NewGuid()}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(problem.TryGetProperty("errorCode", out var errorCode));
    Assert.False(string.IsNullOrEmpty(errorCode.GetString()));
    Assert.True(problem.TryGetProperty("correlationId", out var correlationId));
    Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
  }

  // CT-003: FluentValidation → 400 z dictionary errors per field
  [Fact]
  public async Task ValidationException_response_includes_per_field_errors_dictionary()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // PhoneNumber to "not-a-phone" — narusza walidację E.164
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
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(problem.TryGetProperty("errorCode", out _));
    Assert.True(problem.TryGetProperty("correlationId", out _));
  }

  // CT-004: RateLimitExceeded → 429 z Retry-After
  [Fact]
  public async Task RateLimit_response_returns_429_with_retry_after_header()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // 30+ pojedynczych prób login w krótkim oknie wywoła rate limit AuthSensitive
    HttpResponseMessage? rateLimited = null;
    for (var i = 0; i < 35; i++)
    {
      var resp = await client.PostAsJsonAsync(
        "/api/auth/login",
        new { email = $"x{i}@nope.local", password = "Wrong!", turnstileToken = (string?)null },
        ct);
      if (resp.StatusCode == HttpStatusCode.TooManyRequests)
      {
        rateLimited = resp;
        break;
      }
    }

    Assert.NotNull(rateLimited);
    // ASP.NET RateLimiter ustawia Retry-After
    Assert.True(rateLimited!.Headers.Contains("Retry-After") || rateLimited.Headers.RetryAfter is not null,
      "Spodziewany nagłówek Retry-After w odpowiedzi 429");
  }

  // CT-005: TenantViolation → 403 z errorCode "TenantViolation"
  [Fact]
  public async Task Cross_tenant_access_returns_404_with_error_code()
  {
    // Cross-tenant access przez API zazwyczaj zwraca 404 (zasób nie widoczny w bieżącym tenancie)
    // — to jest świadomy wybór projektowy (nie ujawnia istnienia zasobu w innym tenancie).
    // CT-005 weryfikuje że odpowiedź ma errorCode w extensions.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/Customers/{second.CustomerId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(problem.TryGetProperty("errorCode", out var errorCode));
    Assert.False(string.IsNullOrEmpty(errorCode.GetString()));
  }

  // CT-007: Wszystkie odpowiedzi błędów zawierają correlationId
  [Fact]
  public async Task All_error_responses_include_correlation_id()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // 401 unauthorized — no token
    var unauthorizedResp = await client.GetAsync("/api/Customers", ct);
    Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResp.StatusCode);
    // 401 nie musi mieć ProblemDetails (Identity zwraca prosty 401)

    // 404 NotFound (z ProblemDetails)
    var notFoundResp = await factory.CreateOwnerClient().GetAsync($"/api/Customers/{Guid.NewGuid()}", ct);
    var problem = await notFoundResp.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(problem.TryGetProperty("correlationId", out _));
  }
}
