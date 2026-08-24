using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using App.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Api.IntegrationTests;

/// <summary>
/// White-label: rozwiązanie hosta klienta (rezerwacja.&lt;domena&gt;) na slug/tenant oraz autoryzacja
/// On-Demand TLS (/internal/tls-allowed). W env Testing refresher snapshotu nie startuje (pętle BG są
/// wyłączone), więc po seedzie wołamy RefreshAsync ręcznie — to też potwierdza ścieżkę odświeżania.
/// </summary>
public sealed class WhiteLabelDomainIntegrationTests
{
  private const string Slug = "integration-whitelabel-salon";
  private const string CustomDomain = "integration-whitelabel.pl";
  private const string BookingHost = "rezerwacja." + CustomDomain;
  private const string ApiHost = "api." + CustomDomain;

  private static void SeedTenantWithCustomDomain(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Integration White-Label Salon", Slug);
    tenant.SetCustomDomain(CustomDomain);
    db.Tenants.Add(tenant);
    db.SaveChanges();
  }

  private static async Task RefreshRegistryAsync(IServiceProvider rootServices)
  {
    // Snapshot zasila CORS, resolve-host (pre-check) i tls-allowed. W Testing refresher nie chodzi.
    await rootServices.GetRequiredService<ICustomDomainRegistry>().RefreshAsync();
  }

  [Fact]
  public async Task Resolve_known_booking_host_returns_slug_and_salon_info()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedTenantWithCustomDomain(factory.Services);
    await RefreshRegistryAsync(factory.Services);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/booking-domains/resolve?host={Uri.EscapeDataString(BookingHost)}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var dto = await response.Content.ReadFromJsonAsync<ResolveResponse>(jsonOptions, ct);
    Assert.NotNull(dto);
    Assert.Equal(Slug, dto.Slug);
    Assert.Equal("Integration White-Label Salon", dto.Name);
  }

  [Fact]
  public async Task Resolve_unknown_host_returns_404()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedTenantWithCustomDomain(factory.Services);
    await RefreshRegistryAsync(factory.Services);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      "/api/booking-domains/resolve?host=rezerwacja.cudza-domena.pl", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task TlsAllowed_returns_200_for_registered_hosts_and_404_otherwise()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedTenantWithCustomDomain(factory.Services);
    await RefreshRegistryAsync(factory.Services);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Oba serwowane hosty zarejestrowanej domeny → cert dozwolony.
    var bookingOk = await client.GetAsync($"/internal/tls-allowed?domain={Uri.EscapeDataString(BookingHost)}", ct);
    var apiOk = await client.GetAsync($"/internal/tls-allowed?domain={Uri.EscapeDataString(ApiHost)}", ct);
    Assert.Equal(HttpStatusCode.OK, bookingOk.StatusCode);
    Assert.Equal(HttpStatusCode.OK, apiOk.StatusCode);

    // Niezarejestrowana domena → Caddy NIE wystawi certu.
    var unknown = await client.GetAsync("/internal/tls-allowed?domain=api.losowa-domena.pl", ct);
    Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

    // Apex bez prefiksu rezerwacja./api. (nie serwujemy go) → 404.
    var apex = await client.GetAsync($"/internal/tls-allowed?domain={Uri.EscapeDataString(CustomDomain)}", ct);
    Assert.Equal(HttpStatusCode.NotFound, apex.StatusCode);
  }

  /// <summary>
  /// Refresher chodzi co 5 minut, więc bezwarunkowy log na Information dawał 288 linii na dobę,
  /// każdą z tą samą liczbą — na produkcji był to drugi co do wielkości producent szumu. Sygnał
  /// jest wyłącznie w momencie zmiany zbioru, i to ten kontrakt tu pilnujemy.
  /// </summary>
  [Fact]
  public async Task Refresh_logs_on_information_only_when_domain_set_changes()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedTenantWithCustomDomain(factory.Services);

    var logger = new CapturingLogger<CustomDomainRegistry>();
    var registry = new CustomDomainRegistry(
      factory.Services.GetRequiredService<IServiceScopeFactory>(),
      logger);

    // Pierwszy załadunek po starcie — jedno potwierdzenie, że rejestr wystartował.
    await registry.RefreshAsync();
    Assert.Single(logger.Records, r => r.Level == LogLevel.Information);

    // Cykl bez zmian → schodzi na Debug, w strumieniu produkcyjnym (min. Information) nie widać go wcale.
    await registry.RefreshAsync();
    Assert.Single(logger.Records, r => r.Level == LogLevel.Information);
    Assert.Contains(logger.Records, r => r.Level == LogLevel.Debug);

    // Doszła domena kolejnego klienta → to jest moment, o którym chcemy wiedzieć.
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var second = new Tenant("Integration White-Label Salon 2", Slug + "-2");
      second.SetCustomDomain("integration-whitelabel-druga.pl");
      db.Tenants.Add(second);
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    await registry.RefreshAsync();
    var informational = logger.Records.Where(r => r.Level == LogLevel.Information).ToList();
    Assert.Equal(2, informational.Count);
    Assert.Contains("integration-whitelabel-druga.pl", informational[1].Message);
  }

  private sealed record ResolveResponse(string Name, string Slug);

  private sealed class CapturingLogger<T> : ILogger<T>
  {
    public List<(LogLevel Level, string Message)> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Zawsze true — inaczej nie zobaczylibyśmy linii Debug, a to ona jest dowodem wyciszenia.
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter)
      => Records.Add((logLevel, formatter(state, exception)));
  }
}
