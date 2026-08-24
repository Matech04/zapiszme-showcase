using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

public sealed class HealthCheckIntegrationTests
{
  [Fact]
  public async Task Live_health_returns_healthy_without_authentication()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/health/live", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: ct);
    Assert.NotNull(body);
    Assert.Equal("Healthy", body.Status);
  }

  [Fact]
  public async Task Ready_health_includes_database_check()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/health/ready", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: ct);
    Assert.NotNull(body);
    Assert.Equal("Healthy", body.Status);
    Assert.Contains(body.Checks, check => check.Name == "database" && check.Status == "Healthy");
  }

  [Fact]
  public async Task Live_health_allows_normal_monitor_cadence()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // /health/live jest JEDYNYM publicznie wystawionym health endpointem i ma rate-limit
    // "HealthCheck" (domyślnie 30/min/IP). Kilka kolejnych pingów — kadencja zewnętrznego
    // monitora — mieści się w limicie i zwraca 200.
    for (var i = 0; i < 5; i++)
    {
      var response = await client.GetAsync("/health/live", ct);
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
  }

  [Fact]
  public async Task Live_health_is_rate_limited_against_flood()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:HealthCheck:PermitLimit", "2");
      builder.UseSetting("RateLimiting:HealthCheck:WindowSeconds", "60");
    });
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Po wyczerpaniu okna kolejny request dostaje 429 — limit chroni przed floodem
    // (rejekcja następuje PRZED wykonaniem checku, więc tani DB-free endpoint nie da się
    // użyć jako amplifikacja).
    for (var i = 0; i < 2; i++)
    {
      var ok = await client.GetAsync("/health/live", ct);
      Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    var rejected = await client.GetAsync("/health/live", ct);
    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
  }

  [Fact]
  public async Task Ready_health_is_not_rate_limited()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    for (var i = 0; i < 3; i++)
    {
      var response = await client.GetAsync("/health/ready", ct);
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    var finalResponse = await client.GetAsync("/health/ready", ct);
    Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
  }

  private sealed record HealthResponse(string Status, IReadOnlyCollection<HealthCheckItem> Checks);

  private sealed record HealthCheckItem(string Name, string Status);
}
