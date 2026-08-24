using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Api.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// HOLD-TURNSTILE-001..002 — Invisible Turnstile na publicznym /hold (defense vs distributed
/// botnety, gdzie per-IP rate limit nie wystarcza).
///
/// Domyślnie w środowisku "Testing" verifier zawsze przepuszcza (AcceptingVerifier z DI
/// bazowego). Test "reject" podmienia go lokalnie na RejectingTurnstileVerifier.
/// </summary>
public sealed class PublicHoldTurnstileIntegrationTests
{
  [Fact]
  public async Task Hold_passes_when_turnstile_token_valid_default_testing_verifier()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new
      {
        serviceIds = new[] { seed.ServiceId },
        employeeId = seed.EmployeeId,
        date = TestDates.IsoInDays(41),
        startTime = "10:00:00",
        turnstileToken = "stub-token-testing-env-passes-anyway",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Hold_returns_400_when_turnstile_verification_fails()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.ConfigureTestServices(services =>
      {
        foreach (var d in services.Where(x => x.ServiceType == typeof(ITurnstileVerifier)).ToList())
        {
          services.Remove(d);
        }
        services.AddSingleton<ITurnstileVerifier, RejectingTurnstileVerifier>();
      });
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new
      {
        serviceIds = new[] { seed.ServiceId },
        employeeId = seed.EmployeeId,
        date = TestDates.IsoInDays(42),
        startTime = "10:00:00",
        turnstileToken = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    // Żaden appointment nie powstał — Turnstile blokuje przed Mediator.Send.
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var any = await db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.TenantId == seed.TenantId, ct);
    Assert.False(any);
  }

  /// <summary>
  /// HOLD-TURNSTILE-003 — Invisible Turnstile na confirm-with-session (skip-OTP).
  ///
  /// Regresja z preflightu: to był JEDYNY anonimowy endpoint wyzwalający płatny SMS potwierdzający
  /// bez bot-checku. Capy per-IP ograniczają pojedynczy adres, ale botnet rozsiany po adresach mnożył
  /// je przez liczbę IP i dobijał globalny kill-switch dobowy, ubijając OTP całej platformie.
  ///
  /// Celowo strzelamy w NIEISTNIEJĄCĄ wizytę: gdyby bramka Turnstile zniknęła, żądanie doszłoby do
  /// handlera i zwróciło 404. Odpowiedź 400 dowodzi, że bot-check biegnie PRZED Mediator.Send.
  /// </summary>
  [Fact]
  public async Task ConfirmWithSession_returns_400_when_turnstile_verification_fails()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.ConfigureTestServices(services =>
      {
        foreach (var d in services.Where(x => x.ServiceType == typeof(ITurnstileVerifier)).ToList())
        {
          services.Remove(d);
        }
        services.AddSingleton<ITurnstileVerifier, RejectingTurnstileVerifier>();
      });
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/{Guid.NewGuid()}/confirm-with-session",
      new
      {
        token = Guid.NewGuid(),
        sessionToken = Guid.NewGuid(),
        turnstileToken = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  /// <summary>
  /// Kontrola dopełniająca: przy przepuszczającym verifierze (domyślny w Testing) to samo żądanie
  /// przechodzi bramkę i dociera do handlera — czyli 400 wyżej pochodzi od Turnstile, a nie od
  /// walidacji ciała żądania. Bez tego test powyżej przechodziłby też przy zepsutym kontrakcie.
  /// </summary>
  [Fact]
  public async Task ConfirmWithSession_reaches_handler_when_turnstile_passes()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/{Guid.NewGuid()}/confirm-with-session",
      new
      {
        token = Guid.NewGuid(),
        sessionToken = Guid.NewGuid(),
        turnstileToken = "stub-token-testing-env-passes-anyway",
      },
      ct);

    Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────

  private sealed record SeedResult(string Slug, Guid TenantId, Guid EmployeeId, Guid ServiceId);

  private static SeedResult SeedTenant(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var slug = "ts-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var tenant = new Tenant("Turnstile Hold Salon", slug);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "A", "T", "a@t.local");

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges);
    employee.SetWeeklySchedule(weekly);

    var service = new Service(tenant.Id, category.Id, vat.Id, "S", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, 30, new Money(50m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.SaveChanges();

    return new SeedResult(slug, tenant.Id, employee.Id, service.Id);
  }

  private sealed class RejectingTurnstileVerifier : ITurnstileVerifier
  {
    public Task<bool> VerifyAsync(
      string? token,
      string? remoteIp,
      TurnstileWidgetKind kind = TurnstileWidgetKind.Managed,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(false);
  }
}
