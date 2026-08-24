using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// DEMO-START-* — publiczny tryb demo: anonimowy gość tworzy izolowanego demo-tenanta z danymi
/// i jest od razu „zalogowany" (cookie sesyjne) bez podawania hasła.
///
/// Seeder demo wstawia owned-types (EmployeeService.CustomPrice#Money, Appointment.TotalPrice),
/// których EF InMemory nie wspiera — dlatego te testy wymagają realnego Postgresa
/// (INTEGRATION_DB_PROVIDER=Postgres) i są pomijane w domyślnym przebiegu InMemory.
///
/// Kolekcja `DemoTenants` serializuje wszystkie testy dotykające globalnej puli demo-tenantów
/// (start + cleanup + limit) — bez tego równoległe klasy zaśmiecałyby sobie współdzieloną bazę
/// Postgres (jeden kontener na cały przebieg).
/// </summary>
[Collection("DemoTenants")]
public sealed class DemoStartIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private static readonly bool UsePostgres =
    string.Equals(Environment.GetEnvironmentVariable("INTEGRATION_DB_PROVIDER"), "Postgres", StringComparison.OrdinalIgnoreCase);

  private static WebApplicationFactory<Program> WithDemoEnabled(BookingApiApplicationFactory factory) =>
    factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
      cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Enabled"] = "true" })));

  [Fact]
  public async Task Demo_start_creates_seeded_tenant_and_logs_guest_in_without_password()
  {
    Assert.SkipUnless(UsePostgres, "Wymaga Postgresa (seeder demo używa owned-types nieobsługiwanych przez InMemory).");

    using var factory = new BookingApiApplicationFactory();
    var app = WithDemoEnabled(factory);
    var client = app.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var session = await response.Content.ReadFromJsonAsync<SessionBody>(JsonRead, ct);
    Assert.NotNull(session);
    Assert.True(session!.IsDemo);
    Assert.NotNull(session.TenantId);
    Assert.Contains("Owner", session.Roles);

    // Dane demo zasiane dla tenanta (czytamy z tego samego hosta, który je zapisał).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenantId = session.TenantId!.Value;

    var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(t => t.Id == tenantId, ct);
    Assert.True(tenant.IsDemo);
    Assert.NotNull(tenant.DemoCreatedAtUtc);

    var serviceCount = await db.Services.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, ct);
    var customerCount = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId, ct);
    var appointmentCount = await db.Appointments.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId, ct);
    Assert.Equal(5, serviceCount);
    Assert.Equal(15, customerCount);
    Assert.True(appointmentCount > 0, "Demo powinno mieć zasiane wizyty.");

    // demo/start wystawia cookie sesyjne — gość jest zalogowany bez hasła. (Pełny round-trip
    // /api/auth/me przez cookie nie jest weryfikowalny w env Testing, gdzie domyślnym schematem
    // auth jest nagłówkowy IntegrationTest; tu potwierdzamy samo wystawienie cookie.)
    Assert.True(
      response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(),
      "demo/start powinno wystawić cookie sesyjne.");
  }

  [Fact]
  public async Task Two_demo_starts_create_isolated_tenants()
  {
    Assert.SkipUnless(UsePostgres, "Wymaga Postgresa (seeder demo używa owned-types nieobsługiwanych przez InMemory).");

    using var factory = new BookingApiApplicationFactory();
    var enabled = WithDemoEnabled(factory);
    var ct = TestContext.Current.CancellationToken;

    var first = await StartAsync(enabled.CreateClient(), ct);
    var second = await StartAsync(enabled.CreateClient(), ct);

    Assert.NotEqual(first.TenantId, second.TenantId);
    Assert.NotEqual(first.UserId, second.UserId);
  }

  [Fact]
  public async Task Demo_start_returns_404_when_demo_disabled()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient(); // Demo:Enabled nie ustawione → default false
    var ct = TestContext.Current.CancellationToken;

    var enabled = await client.GetFromJsonAsync<EnabledBody>("/api/demo/enabled", JsonRead, ct);
    Assert.NotNull(enabled);
    Assert.False(enabled!.Enabled);

    var response = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static async Task<SessionBody> StartAsync(HttpClient client, CancellationToken ct)
  {
    var response = await client.PostAsync("/api/demo/start", content: null, ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var session = await response.Content.ReadFromJsonAsync<SessionBody>(JsonRead, ct);
    Assert.NotNull(session);
    return session!;
  }

  private sealed record SessionBody(
    Guid UserId,
    string Email,
    string DisplayName,
    string[] Roles,
    Guid? TenantId,
    Guid? EmployeeId,
    bool IsDemo);

  private sealed record EnabledBody(bool Enabled);
}
