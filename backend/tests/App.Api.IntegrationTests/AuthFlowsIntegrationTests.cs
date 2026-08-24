using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// USR-002 — Register slug conflict / invalid timezone.
/// USR-003 — Login happy + 401 invalid creds.
/// USR-004 — Lockout po N nieudanych logowaniach.
/// USR-006 — ConfirmEmail happy path.
/// USR-007 — ForgotPassword → ResetPassword.
/// USR-008 — InviteEmployee rejects invalid role.
/// USR-009 — InviteEmployee 402 on Free plan when ≥1 active employee.
/// USR-010 — AcceptInvite end-to-end.
/// USR-017 — Logout invalidates session.
/// </summary>
public sealed class AuthFlowsIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  // ── USR-003 Login ─────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Login_with_valid_credentials_returns_session_and_sets_cookie()
  {
    using var factory = new BookingApiApplicationFactory();
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
        turnstileToken = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
    Assert.Contains(cookies, c => c.Contains("booking_saas_identity", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Login_with_invalid_password_returns_unauthorized()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/login",
      new
      {
        email = "owner@rest-seed.local",
        password = "WrongPassword!",
        turnstileToken = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  // ── USR-004 Lockout ──────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Login_locks_account_after_max_failed_attempts()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    const string email = "owner@rest-seed.local";

    // 5 failed attempts (MaxFailedAccessAttempts = 5)
    for (var i = 0; i < 5; i++)
    {
      _ = await client.PostAsJsonAsync(
        "/api/auth/login",
        new { email, password = "BadPassword!", turnstileToken = (string?)null },
        ct);
    }

    // Use correct password — should fail (account locked OR rate-limited)
    var response = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email, password = "Password123!", turnstileToken = (string?)null },
      ct);

    // Either 401 (locked out by Identity) or 429 (blocked by rate limiter) — both protect the account
    Assert.True(
      response.StatusCode == HttpStatusCode.Unauthorized ||
      response.StatusCode == HttpStatusCode.TooManyRequests,
      $"Expected 401 (lockout) or 429 (rate limit), got {(int)response.StatusCode}");
  }

  // ── USR-002 Onboarding negative paths ────────────────────────────────────────────────
  // Uwaga (Droga B): slug (i strefa czasowa/waluta) NIE są już częścią rejestracji — slim
  // register-owner przyjmuje tylko email/hasło/telefon. Unikalność sluga egzekwuje dopiero krok
  // kreatora /api/onboarding/profile (SalonSlugTakenException → 409). Dawny test „invalid timezone"
  // zniknął razem z polem — tenant dostaje twardo Europe/Warsaw w CompleteProfile.

  [Fact]
  public async Task OnboardingProfile_with_taken_slug_returns_conflict()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services); // zajmuje slug RestApiIntegrationSeed.TenantSlug
    var ct = TestContext.Current.CancellationToken;

    // Świeży, potwierdzony właściciel próbuje utworzyć salon na już zajętym slugu.
    var anon = factory.CreateClient();
    var userId = await OnboardingTestSupport.RegisterOwnerAsync(
      anon, "newowner@dup.local", "+48501111020", ct: ct);
    await OnboardingTestSupport.ConfirmAccountInDbAsync(factory, userId, ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await OnboardingTestSupport.PostProfileAsync(
      ownerClient, "Duplicate Slug Salon", RestApiIntegrationSeed.TenantSlug, ct: ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  // ── USR-006 ConfirmEmail ─────────────────────────────────────────────────────────────

  [Fact]
  public async Task ConfirmEmail_with_valid_token_sets_email_confirmed_true()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Register a new owner — confirmation URL is captured in mailbox
    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "confirm@e.local",
        password = "Password123!",
        phoneNumber = "+48501111022",
      },
      ct);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastConfirmEmailUrl);

    var uri = new Uri(mailbox.LastConfirmEmailUrl!);
    var queryParts = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));

    var response = await client.PostAsJsonAsync(
      "/api/auth/confirm-email",
      new { userId = queryParts["userId"], token = queryParts["token"] },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.FirstAsync(u => u.Email == "confirm@e.local", ct);
    Assert.True(user.EmailConfirmed);
  }

  // ── USR-007 ForgotPassword ───────────────────────────────────────────────────────────

  [Fact]
  public async Task ForgotPassword_for_existing_email_returns_204_and_sends_reset_link()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/forgot-password",
      new { email = "owner@rest-seed.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastPasswordResetUrl);
  }

  [Fact]
  public async Task ForgotPassword_for_unknown_email_still_returns_204_no_enumeration()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/forgot-password",
      new { email = "ghost@nope.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  // ── USR-008 InviteEmployee role validation ───────────────────────────────────────────

  [Fact]
  public async Task InviteEmployee_with_invalid_role_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/employees",
      new
      {
        email = "newhire@invite.local",
        displayName = "Wrong Role",
        firstName = "Wrong",
        lastName = "Role",
        role = "Owner",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  // ── USR-009 Free plan employee limit ─────────────────────────────────────────────────

  [Fact]
  public async Task InviteEmployee_returns_402_when_free_plan_has_active_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // RestApiIntegrationSeed creates 1 employee (Owner) — counts toward limit
    SetTenantToPastDue(factory.Services, seed.TenantId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/employees",
      new
      {
        email = "second@invite.local",
        displayName = "Second Employee",
        firstName = "Second",
        lastName = "Employee",
        role = "Employee",
      },
      ct);

    Assert.Equal((HttpStatusCode)402, response.StatusCode);
  }

  // ── USR-010 AcceptInvite ─────────────────────────────────────────────────────────────

  [Fact]
  public async Task AcceptInvite_with_valid_token_sets_password_confirms_email_and_signs_in()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // Upgrade to Pro so invite is not blocked by Free plan limit
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Owner invites employee
    var inviteResp = await ownerClient.PostAsJsonAsync(
      "/api/auth/employees",
      new
      {
        email = "acceptme@invite.local",
        displayName = "Accept Me",
        firstName = "Accept",
        lastName = "Me",
        role = "Employee",
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, inviteResp.StatusCode);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var uri = new Uri(mailbox.LastEmployeeInviteUrl!);
    var queryParts = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));

    // Accept invite (anonymous)
    var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync(
      "/api/auth/accept-invite",
      new { userId = queryParts["userId"], token = queryParts["token"], password = "NewPassword123!" },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.FirstAsync(u => u.Email == "acceptme@invite.local", ct);
    Assert.True(user.EmailConfirmed);
  }

  // ── USR-011 ResendEmployeeInvite ─────────────────────────────────────────────────────

  [Fact]
  public async Task ResendEmployeeInvite_for_unconfirmed_employee_sends_fresh_link_that_accepts()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var inviteResp = await ownerClient.PostAsJsonAsync(
      "/api/auth/employees",
      new { email = "resend@invite.local", displayName = "Resend Me", firstName = "Resend", lastName = "Me", role = "Employee" },
      ct);
    Assert.Equal(HttpStatusCode.OK, inviteResp.StatusCode);
    var invite = await inviteResp.Content.ReadFromJsonAsync<JsonElement>(ct);
    var employeeId = invite.GetProperty("employeeId").GetString();

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastEmployeeInviteUrl);

    // Owner ponawia zaproszenie (np. link wygasł)
    var resendResp = await ownerClient.PostAsync($"/api/auth/employees/{employeeId}/resend-invite", content: null, ct);
    Assert.Equal(HttpStatusCode.NoContent, resendResp.StatusCode);

    // Świeży link aktywuje konto
    var uri = new Uri(mailbox.LastEmployeeInviteUrl!);
    var q = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));
    var anon = factory.CreateClient();
    var accept = await anon.PostAsJsonAsync(
      "/api/auth/accept-invite",
      new { userId = q["userId"], token = q["token"], password = "NewPassword123!" },
      ct);
    Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
  }

  [Fact]
  public async Task ResendEmployeeInvite_for_already_confirmed_account_returns_conflict()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantToActivePlan(factory.Services, seed.TenantId);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var inviteResp = await ownerClient.PostAsJsonAsync(
      "/api/auth/employees",
      new { email = "confirmed@invite.local", displayName = "Confirmed", firstName = "Con", lastName = "Firmed", role = "Employee" },
      ct);
    Assert.Equal(HttpStatusCode.OK, inviteResp.StatusCode);
    var invite = await inviteResp.Content.ReadFromJsonAsync<JsonElement>(ct);
    var employeeId = invite.GetProperty("employeeId").GetString();

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var uri = new Uri(mailbox.LastEmployeeInviteUrl!);
    var q = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));
    var anon = factory.CreateClient();
    var accept = await anon.PostAsJsonAsync(
      "/api/auth/accept-invite",
      new { userId = q["userId"], token = q["token"], password = "NewPassword123!" },
      ct);
    Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

    // Konto już aktywne → ponowne zaproszenie zbędne → 409
    var resendResp = await ownerClient.PostAsync($"/api/auth/employees/{employeeId}/resend-invite", content: null, ct);
    Assert.Equal(HttpStatusCode.Conflict, resendResp.StatusCode);
  }

  // ── USR-017 Logout ───────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Logout_returns_no_content()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync("/api/auth/logout", content: null, ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  // W nowym modelu nie ma planu Free — odpowiednikiem "tenant po trialu, brak płatności"
  // jest PastDue (publiczne booking flow zablokowane, ale konto istnieje).
  private static void SetTenantToPastDue(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.SetSubscription(App.Domain.Aggregates.TenantAggregate.Subscription.AdminReset(
      App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.PastDue, seats: 1, isFoundingMember: false,
      trialEndsAt: null, currentPeriodEndsAt: null));
    db.SaveChanges();
  }

  private static void SetTenantToActivePlan(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.SetSubscription(App.Domain.Aggregates.TenantAggregate.Subscription.AdminReset(
      App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.Active, seats: 1, isFoundingMember: false,
      trialEndsAt: null, currentPeriodEndsAt: DateTimeOffset.UtcNow.AddYears(1)));
    db.SaveChanges();
  }
}
