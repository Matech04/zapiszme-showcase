using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Controllers;
using App.Api.E2eSupport;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Api.IntegrationTests;

/// <summary>
/// DEMO-SEC-* — kontrole bezpieczeństwa/anty-abuse trybu demo:
/// (1) endpoint jest anonimowy ale rate-limited (AuthSensitive),
/// (2) twardy limit liczby żywych demo-tenantów (503),
/// (3) cleanup hard-kasuje cały graf demo na realnym Postgresie bez naruszeń FK.
///
/// Współdzieli kolekcję `DemoTenants` z <see cref="DemoStartIntegrationTests"/> — testy globalnej
/// puli demo muszą iść sekwencyjnie (jeden kontener Postgres na przebieg).
/// </summary>
[Collection("DemoTenants")]
public sealed class DemoSecurityIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private static readonly bool UsePostgres =
    string.Equals(Environment.GetEnvironmentVariable("INTEGRATION_DB_PROVIDER"), "Postgres", StringComparison.OrdinalIgnoreCase);

  private static WebApplicationFactory<Program> WithDemoConfig(
    BookingApiApplicationFactory factory, Dictionary<string, string?> overrides) =>
    factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(overrides)));

  // ── (1) Anonimowy + rate-limited — bez bazy, czysta refleksja na atrybutach kontrolera ──

  [Fact]
  public void Start_endpoint_is_anonymous_and_rate_limited()
  {
    var start = typeof(DemoController).GetMethod(nameof(DemoController.Start))!;

    Assert.Single(start.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));

    var rateLimit = start.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
      .Cast<EnableRateLimitingAttribute>()
      .SingleOrDefault();
    Assert.NotNull(rateLimit);
    Assert.Equal("AuthSensitive", rateLimit!.PolicyName);
  }

  [Fact]
  public void Enabled_endpoint_is_anonymous()
  {
    var enabled = typeof(DemoController).GetMethod(nameof(DemoController.Enabled))!;
    Assert.Single(enabled.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
  }

  // ── (2) Twardy limit liczby żywych demo-tenantów → 503 ──

  [Fact]
  public async Task Start_returns_503_when_live_demo_cap_reached()
  {
    Assert.SkipUnless(UsePostgres, "Wymaga Postgresa (seeder demo używa owned-types nieobsługiwanych przez InMemory).");

    using var factory = new BookingApiApplicationFactory();
    var app = WithDemoConfig(factory, new() { ["Demo:Enabled"] = "true", ["Demo:MaxLiveTenants"] = "1" });
    var ct = TestContext.Current.CancellationToken;

    // Baseline: usuń wszystkie istniejące demo-tenanty (leftover z innych testów w kolekcji).
    await PurgeAllDemoTenantsAsync(app, ct);

    var client = app.CreateClient();

    var first = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);

    // Limit = 1, jeden żywy demo-tenant → kolejny start odrzucony 503.
    var second = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
  }

  // ── (3) Cleanup hard-kasuje cały graf demo (Postgres, realne FK) ──

  [Fact]
  public async Task Cleanup_hard_deletes_entire_demo_graph()
  {
    Assert.SkipUnless(UsePostgres, "Wymaga Postgresa (seeder demo używa owned-types nieobsługiwanych przez InMemory).");

    using var factory = new BookingApiApplicationFactory();
    var app = WithDemoConfig(factory, new() { ["Demo:Enabled"] = "true", ["Demo:MaxLiveTenants"] = "1000" });
    var ct = TestContext.Current.CancellationToken;

    await PurgeAllDemoTenantsAsync(app, ct);

    var client = app.CreateClient();
    var response = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var session = await response.Content.ReadFromJsonAsync<SessionBody>(JsonRead, ct);
    Assert.NotNull(session);
    var tenantId = session!.TenantId!.Value;
    var userId = session.UserId;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Sanity: dane istnieją przed cleanupem.
    Assert.True(await db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId, ct));

    // TTL=24h, "teraz"=+48h → demo-tenant (utworzony ~now) jest poza oknem → kasowany.
    await DemoTenantCleanupHostedService.RunCycleAsync(
      db, DateTime.UtcNow.AddHours(48), TimeSpan.FromHours(24), ct, NullLogger.Instance);

    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId, ct));
    Assert.False(await db.Services.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId, ct));
    Assert.False(await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId, ct));
    Assert.False(await db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId, ct));
  }

  private static async Task PurgeAllDemoTenantsAsync(WebApplicationFactory<Program> app, CancellationToken ct)
  {
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // TTL=0, "teraz" w przyszłości → cutoff za wszystkimi demo → kasuje całą pulę (baseline 0).
    await DemoTenantCleanupHostedService.RunCycleAsync(
      db, DateTime.UtcNow.AddYears(10), TimeSpan.Zero, ct, NullLogger.Instance);
  }

  private sealed record SessionBody(Guid UserId, Guid? TenantId, bool IsDemo);
}
