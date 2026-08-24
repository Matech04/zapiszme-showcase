using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Security;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace App.Api.IntegrationTests;

public sealed class AuthApiIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  // Droga B: slim rejestracja tworzy WYŁĄCZNIE konto Identity (Owner, niepotwierdzone). Tenant +
  // Employee + VAT powstają dopiero w kroku kreatora (/api/onboarding/profile), więc po samej
  // rejestracji NIE MOŻE istnieć ani tenant, ani pracownik. Sesji też brak (wymagamy confirm),
  // a mail potwierdzający wychodzi.
  [Fact]
  public async Task Register_owner_creates_account_without_tenant_and_without_auto_signin()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "owner@new-owner.local",
        password = "Password123!",
        phoneNumber = "+48501111010",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Backend nie tworzy sesji — żadnego identity cookie do panelu nie powinno wracać.
    var hadCookies = response.Headers.TryGetValues("Set-Cookie", out var cookies);
    if (hadCookies)
    {
      Assert.DoesNotContain(cookies!, c => c.Contains("booking_saas_identity", StringComparison.Ordinal));
    }

    var body = await response.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);
    Assert.NotNull(body);
    Assert.Equal("owner@new-owner.local", body!.Email);
    Assert.False(body.EmailConfirmed);
    Assert.NotEqual(Guid.Empty, body.UserId);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.True(await db.Users.AnyAsync(u => u.Id == body.UserId, ct));

    // Kluczowa różnica Drogi B — rejestracja NIE tworzy jeszcze tenanta ani pracownika.
    Assert.False(
      await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.UserId == body.UserId, ct),
      "Rejestracja nie może tworzyć Employee — to robi dopiero /onboarding/profile");

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.StartsWith("http://localhost:4200/confirm-email?", mailbox.LastConfirmEmailUrl);
    Assert.Contains($"userId={body.UserId}", mailbox.LastConfirmEmailUrl);
  }

  private sealed record RegisterOwnerResponseBody(Guid UserId, string Email, bool EmailConfirmed);

  [Fact]
  public async Task Register_owner_rejects_failed_turnstile_verification()
  {
    using var factory = new BookingApiApplicationFactory()
      .WithWebHostBuilder(builder =>
      {
        builder.ConfigureTestServices(services =>
        {
          foreach (var descriptor in services.Where(x => x.ServiceType == typeof(ITurnstileVerifier)).ToList())
          {
            services.Remove(descriptor);
          }

          services.AddSingleton<ITurnstileVerifier, RejectingTurnstileVerifier>();
        });
      });
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "owner@blocked-owner.local",
        password = "Password123!",
        phoneNumber = "+48501111011",
        turnstileToken = "invalid",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Register_owner_slug_availability_returns_true_when_slug_unused()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;
    var slug = "free-slug-" + Guid.NewGuid().ToString("N")[..10];

    var response = await client.GetAsync(
      $"/api/auth/register-owner/slug-availability?slug={Uri.EscapeDataString(slug)}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.True(json.GetProperty("available").GetBoolean());
  }

  [Fact]
  public async Task Register_owner_slug_availability_returns_false_when_slug_taken()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/auth/register-owner/slug-availability?slug={Uri.EscapeDataString(RestApiIntegrationSeed.TenantSlug)}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonRead, ct);
    Assert.False(json.GetProperty("available").GetBoolean());
  }

  [Fact]
  public async Task Register_owner_slug_availability_returns_bad_request_for_invalid_slug()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      "/api/auth/register-owner/slug-availability?slug=" + Uri.EscapeDataString("bad slug"),
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Login_rejects_failed_turnstile_verification()
  {
    using var factory = new BookingApiApplicationFactory()
      .WithWebHostBuilder(builder =>
      {
        builder.ConfigureTestServices(services =>
        {
          foreach (var descriptor in services.Where(x => x.ServiceType == typeof(ITurnstileVerifier)).ToList())
          {
            services.Remove(descriptor);
          }

          services.AddSingleton<ITurnstileVerifier, RejectingTurnstileVerifier>();
        });
      });
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/login",
      new
      {
        email = "owner@rest-seed.local",
        password = "Password123!",
        turnstileToken = "invalid",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Me_returns_current_identity_session_for_seeded_owner()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/auth/me", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var session = await response.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonRead, ct);
    Assert.NotNull(session);
    Assert.Equal(IntegrationTestUserIds.SalonOwner, session.UserId);
    Assert.Equal(seed.TenantId, session.TenantId);
    Assert.Equal(seed.EmployeeId, session.EmployeeId);
    Assert.Contains("Owner", session.Roles);
  }

  [Fact]
  public async Task Owner_can_invite_employee_account()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // Seed daje Free plan + 1 employee; bez Pro inviteEmployee zwróci 402 (limit planu).
    UpgradeTenantToPro(factory.Services, seed.TenantId);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/employees",
      new
      {
        email = "employee@rest-seed.local",
        displayName = "Employee Seed",
        firstName = "Employee",
        lastName = "Seed",
        role = "Employee",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var account = await response.Content.ReadFromJsonAsync<EmployeeInviteResponse>(JsonRead, ct);
    Assert.NotNull(account);
    Assert.Equal("Employee", account.Role);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == account.EmployeeId, ct);
    Assert.Equal(seed.TenantId, employee.TenantId);
    Assert.Equal(account.UserId, employee.UserId);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.StartsWith("http://localhost:4200/accept-invite?", mailbox.LastEmployeeInviteUrl);
    Assert.Contains($"userId={account.UserId}", mailbox.LastEmployeeInviteUrl);
  }

  [Fact]
  public async Task Login_with_rememberMe_issues_persistent_cookie_otherwise_session_cookie()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:Global:PermitLimit", "100");
      builder.UseSetting("RateLimiting:Auth:PermitLimit", "100");
    });
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    var ct = TestContext.Current.CancellationToken;

    // rememberMe=true → trwałe cookie z datą wygaśnięcia ~30 dni.
    var persistentResp = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "owner@rest-seed.local", password = "Password123!", rememberMe = true },
      ct);
    Assert.Equal(HttpStatusCode.OK, persistentResp.StatusCode);
    var persistentCookie = GetIdentityCookie(persistentResp);
    Assert.NotNull(persistentCookie.Expires);
    var lifetime = persistentCookie.Expires!.Value - DateTimeOffset.UtcNow;
    Assert.True(
      lifetime > TimeSpan.FromDays(29) && lifetime < TimeSpan.FromDays(31),
      $"Oczekiwano trwałości ~30 dni, było {lifetime}.");

    // rememberMe=false → cookie sesyjne (bez Expires/Max-Age, znika po zamknięciu przeglądarki).
    var sessionResp = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "owner@rest-seed.local", password = "Password123!", rememberMe = false },
      ct);
    Assert.Equal(HttpStatusCode.OK, sessionResp.StatusCode);
    var sessionCookie = GetIdentityCookie(sessionResp);
    Assert.Null(sessionCookie.Expires);
    Assert.Null(sessionCookie.MaxAge);
  }

  private static SetCookieHeaderValue GetIdentityCookie(HttpResponseMessage response)
  {
    Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
    var raw = cookies!.Single(c => c.StartsWith("booking_saas_identity=", StringComparison.Ordinal));
    return SetCookieHeaderValue.Parse(raw);
  }

  private static void UpgradeTenantToPro(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.SetSubscription(App.Domain.Aggregates.TenantAggregate.Subscription.AdminReset(
      App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.Active, seats: 1, isFoundingMember: false,
      trialEndsAt: null, currentPeriodEndsAt: DateTimeOffset.UtcNow.AddYears(1)));
    db.SaveChanges();
  }

  private sealed record AuthSessionResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    Guid? TenantId,
    Guid? EmployeeId);

  private sealed record EmployeeInviteResponse(Guid UserId, Guid EmployeeId, string Role);

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
