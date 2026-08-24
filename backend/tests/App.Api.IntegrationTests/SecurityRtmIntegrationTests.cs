using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// RTM Security — testy integracyjne dla scenariuszy o najwyższym ROI:
///
/// SEC-002 — AuthSensitive policy odrzuca request po przekroczeniu limitu (per-IP).
/// SEC-005 — FeedbackWrite policy (5/10min) — 6. request 429.
/// SEC-032 — ResetPassword token single-use (drugie wywołanie → 400).
/// SEC-033 — AcceptInvite token single-use (drugie wywołanie → 400).
/// SEC-038 — Login zwraca jednolite 401 dla nieznanego maila i błędnego hasła (anti-enum).
/// SEC-042 — SearchCustomers searchTerm "%" / "_" nie jest LIKE-wildcardem.
/// SEC-046 — JSON deserialization: nieznane pola ignorowane, malformed JSON → 400.
/// SEC-048 — /api/auth/me NIE zwraca PasswordHash / SecurityStamp / ConcurrencyStamp.
/// SEC-050 — Cookie booking_saas_identity ma HttpOnly + SameSite (Secure tylko nad HTTPS w prod).
/// </summary>
public sealed class SecurityRtmIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  // ── SEC-002 ─────────────────────────────────────────────────────────────────────────
  // AuthSensitive policy odrzuca request po przekroczeniu limitu. PermitLimit
  // sterowalny przez RateLimiting:Auth:PermitLimit — ustawiamy małe N, żeby test był szybki.

  [Fact]
  public async Task AuthSensitive_policy_rejects_login_requests_after_permit_limit_exceeded_with_429()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:Auth:PermitLimit", "3");
      builder.UseSetting("RateLimiting:Auth:WindowSeconds", "60");
    });
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Pierwsze 3 powinny przejść do handlera (401 invalid creds) — limit jeszcze nie wykorzystany
    for (var i = 0; i < 3; i++)
    {
      var response = await client.PostAsJsonAsync(
        "/api/auth/login",
        new { email = "owner@rest-seed.local", password = "BadPassword!", turnstileToken = (string?)null },
        ct);
      Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // 4-ty request — limit przekroczony, AuthSensitive zwraca 429
    var rejected = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "owner@rest-seed.local", password = "BadPassword!", turnstileToken = (string?)null },
      ct);

    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    Assert.True(rejected.Headers.TryGetValues("Retry-After", out _),
      "AuthSensitive 429 powinno mieć nagłówek Retry-After");
  }

  // ── SEC-005 ─────────────────────────────────────────────────────────────────────────
  // FeedbackWrite policy (5/10min) — partycja per-user dla authenticated. 6. request 429.

  [Fact]
  public async Task FeedbackWrite_policy_rejects_sixth_feedback_request_in_window()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      // Testing env ustawia Global:PermitLimit=3 — podnosimy aby NIE on cuttował,
      // tylko FeedbackWrite (hard-coded 5/10min) ograniczał.
      builder.UseSetting("RateLimiting:Global:PermitLimit", "100");
    });
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.UserId, IntegrationTestUserIds.SalonOwner.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    var ct = TestContext.Current.CancellationToken;

    for (var i = 0; i < 5; i++)
    {
      var resp = await client.PostAsJsonAsync(
        "/api/Feedback",
        new
        {
          kind = "Bug",
          title = $"Feedback {i}",
          description = "Test feedback",
          pageUrl = (string?)null,
        },
        ct);
      Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
    }

    var rejected = await client.PostAsJsonAsync(
      "/api/Feedback",
      new
      {
        kind = "Bug",
        title = "Feedback 6",
        description = "Test feedback",
        pageUrl = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
  }

  // ── SEC-032 ─────────────────────────────────────────────────────────────────────────
  // ResetPassword token single-use — Identity rotuje SecurityStamp po ResetPasswordAsync,
  // więc ten sam token w drugim wywołaniu nie może już zweryfikować się.

  [Fact]
  public async Task ResetPassword_token_is_single_use_second_attempt_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var forgot = await client.PostAsJsonAsync(
      "/api/auth/forgot-password",
      new { email = "owner@rest-seed.local" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastPasswordResetUrl);

    var (userId, token) = ParseUserIdAndToken(mailbox.LastPasswordResetUrl!);

    // Pierwsze użycie tokenu — sukces
    var firstReset = await client.PostAsJsonAsync(
      "/api/auth/reset-password",
      new { userId, token, password = "NewPasswordA1!" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, firstReset.StatusCode);

    // Drugie użycie tego samego tokenu — odrzucone, bo SecurityStamp już zmieniony
    var secondReset = await client.PostAsJsonAsync(
      "/api/auth/reset-password",
      new { userId, token, password = "NewPasswordB2!" },
      ct);
    Assert.Equal(HttpStatusCode.BadRequest, secondReset.StatusCode);
  }

  // ── SEC-033 ─────────────────────────────────────────────────────────────────────────
  // AcceptInvite używa tej samej mechaniki ResetPasswordAsync — token też single-use.

  [Fact]
  public async Task AcceptInvite_token_is_single_use_second_attempt_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToProPlan(factory.Services, seed.TenantId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var inviteResp = await ownerClient.PostAsJsonAsync(
      "/api/auth/employees",
      new
      {
        email = "sec033@invite.local",
        displayName = "Single Use",
        firstName = "SingleUse",
        lastName = "Token",
        role = "Employee",
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, inviteResp.StatusCode);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastEmployeeInviteUrl);

    var (userId, token) = ParseUserIdAndToken(mailbox.LastEmployeeInviteUrl!);

    var anonClient = factory.CreateClient();
    var firstAccept = await anonClient.PostAsJsonAsync(
      "/api/auth/accept-invite",
      new { userId, token, password = "InvitePasswordA1!" },
      ct);
    Assert.Equal(HttpStatusCode.OK, firstAccept.StatusCode);

    var secondAccept = await anonClient.PostAsJsonAsync(
      "/api/auth/accept-invite",
      new { userId, token, password = "InvitePasswordB2!" },
      ct);
    Assert.Equal(HttpStatusCode.BadRequest, secondAccept.StatusCode);
  }

  // ── SEC-038 ─────────────────────────────────────────────────────────────────────────
  // Anti-enum: 401 dla NIEISTNIEJĄCEGO emaila i 401 dla błędnego hasła powinny
  // dawać identyczny ProblemDetails (status, title, errorCode) — atakujący nie może
  // odróżnić "konto nie istnieje" od "hasło złe".

  [Fact]
  public async Task Login_returns_uniform_401_for_unknown_email_and_wrong_password()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var unknownEmail = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "no-such-user@nowhere.local", password = "Whatever123!", turnstileToken = (string?)null },
      ct);
    var wrongPassword = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "owner@rest-seed.local", password = "DefinitelyWrong!", turnstileToken = (string?)null },
      ct);

    Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

    var unknownJson = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>(ct);
    var wrongJson = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(ct);

    // Status i title muszą być identyczne
    Assert.Equal(GetStringProperty(unknownJson, "status"), GetStringProperty(wrongJson, "status"));
    Assert.Equal(GetStringProperty(unknownJson, "title"), GetStringProperty(wrongJson, "title"));
  }

  // ── SEC-042 ─────────────────────────────────────────────────────────────────────────
  // SearchCustomers searchTerm "%" / "_" nie jest LIKE-wildcardem. Klient ze znanym
  // FirstName "Jan" istnieje, ale searchTerm "%" nie zwraca go (nie ma % w treści).

  [Fact]
  public async Task SearchCustomers_with_percent_wildcard_does_not_match_all_records()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Sanity: znany searchTerm "Jan" zwraca seeded customer
    var jan = await client.GetAsync("/api/Customers/search?searchTerm=Jan", ct);
    Assert.Equal(HttpStatusCode.OK, jan.StatusCode);
    var janResults = await jan.Content.ReadFromJsonAsync<List<JsonElement>>(JsonRead, ct);
    Assert.NotNull(janResults);
    Assert.NotEmpty(janResults);

    // searchTerm "%" — jeśli traktowane jako LIKE-wildcard, wszystkie rekordy by przeszły.
    // Oczekujemy: literalny "%" — żadne imię/nazwisko/telefon w seedzie nie zawiera "%".
    var percent = await client.GetAsync("/api/Customers/search?searchTerm=%25", ct);
    Assert.Equal(HttpStatusCode.OK, percent.StatusCode);
    var percentResults = await percent.Content.ReadFromJsonAsync<List<JsonElement>>(JsonRead, ct);
    Assert.NotNull(percentResults);
    Assert.Empty(percentResults);

    // To samo dla "_" (LIKE single-char wildcard)
    var underscore = await client.GetAsync("/api/Customers/search?searchTerm=_", ct);
    Assert.Equal(HttpStatusCode.OK, underscore.StatusCode);
    var underscoreResults = await underscore.Content.ReadFromJsonAsync<List<JsonElement>>(JsonRead, ct);
    Assert.NotNull(underscoreResults);
    Assert.Empty(underscoreResults);
  }

  // ── SEC-046 ─────────────────────────────────────────────────────────────────────────
  // JSON deserialization: nieznane pola ignorowane (default), malformed JSON → 400.

  [Fact]
  public async Task Json_deserializer_ignores_unknown_properties_in_login_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Dodatkowe pole isAdmin / extraInjected — nie powinno spowodować 400
    var bodyWithUnknown = """
      {
        "email": "owner@rest-seed.local",
        "password": "Password123!",
        "turnstileToken": null,
        "isAdmin": true,
        "extraInjected": "x"
      }
      """;

    var resp = await client.PostAsync(
      "/api/auth/login",
      new StringContent(bodyWithUnknown, Encoding.UTF8, "application/json"),
      ct);

    // Login musi się powieść (200) — unknown properties tylko ignorowane, nie wpłynęły na bind
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }

  [Fact]
  public async Task Json_malformed_payload_returns_400_problem_details()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Złamany JSON (trailing comma + brak zamykającego })
    var malformed = "{ \"email\": \"x@y.z\", \"password\": \"abc\",";

    var resp = await client.PostAsync(
      "/api/auth/login",
      new StringContent(malformed, Encoding.UTF8, "application/json"),
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  // ── SEC-048 ─────────────────────────────────────────────────────────────────────────
  // /api/auth/me NIE zwraca pól wewnętrznych Identity (PasswordHash, SecurityStamp,
  // ConcurrencyStamp). Weryfikujemy raw JSON klucze.

  [Fact]
  public async Task AuthMe_response_does_not_leak_internal_identity_fields()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var resp = await client.GetAsync("/api/auth/me", ct);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    var body = await resp.Content.ReadAsStringAsync(ct);
    AssertCaseInsensitiveDoesNotContain(body, "passwordHash");
    AssertCaseInsensitiveDoesNotContain(body, "securityStamp");
    AssertCaseInsensitiveDoesNotContain(body, "concurrencyStamp");
    AssertCaseInsensitiveDoesNotContain(body, "phoneNumber"); // nie ujawniamy phoneNumber Identity
    AssertCaseInsensitiveDoesNotContain(body, "lockoutEnd");
    AssertCaseInsensitiveDoesNotContain(body, "accessFailedCount");
  }

  // ── SEC-050 ─────────────────────────────────────────────────────────────────────────
  // Auth cookie booking_saas_identity ma HttpOnly + SameSite. Secure flag wymagany
  // tylko w prod (HTTPS); w testach (HTTP) nie sprawdzamy Secure.

  [Fact]
  public async Task Login_response_sets_identity_cookie_with_httponly_and_samesite_flags()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var resp = await client.PostAsJsonAsync(
      "/api/auth/login",
      new
      {
        email = "owner@rest-seed.local",
        password = "Password123!",
        turnstileToken = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
    var identityCookie = cookies!.FirstOrDefault(c => c.Contains("booking_saas_identity", StringComparison.Ordinal));
    Assert.NotNull(identityCookie);

    // HttpOnly chroni przed XSS-em (JavaScript nie odczyta cookie)
    Assert.Contains("httponly", identityCookie!, StringComparison.OrdinalIgnoreCase);
    // SameSite chroni przed cross-site CSRF
    Assert.Contains("samesite", identityCookie, StringComparison.OrdinalIgnoreCase);
  }

  // ── helpers ─────────────────────────────────────────────────────────────────────────

  private static (Guid userId, string token) ParseUserIdAndToken(string url)
  {
    var uri = new Uri(url);
    var parts = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));
    return (Guid.Parse(parts["userId"]), parts["token"]);
  }

  private static string? GetStringProperty(JsonElement element, string name)
  {
    if (!element.TryGetProperty(name, out var prop))
    {
      return null;
    }
    return prop.ValueKind switch
    {
      JsonValueKind.String => prop.GetString(),
      JsonValueKind.Number => prop.GetRawText(),
      _ => prop.ToString(),
    };
  }

  private static void AssertCaseInsensitiveDoesNotContain(string body, string fragment)
  {
    Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
  }

  private static void SetTenantToProPlan(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<App.Infrastructure.Persistence.ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.SetSubscription(App.Domain.Aggregates.TenantAggregate.Subscription.AdminReset(
      App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.Active, seats: 1, isFoundingMember: false,
      trialEndsAt: null, currentPeriodEndsAt: DateTimeOffset.UtcNow.AddYears(1)));
    db.SaveChanges();
  }
}
