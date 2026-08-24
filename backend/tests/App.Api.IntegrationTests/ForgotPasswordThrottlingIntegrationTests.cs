using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// SEC-009 — per-email throttling na /api/auth/forgot-password.
///
/// Cel: niezależnie od liczby IP / botnetu, JEDEN adres email nie powinien dostawać
/// więcej niż 1 mail-reset w oknie cooldown (domyślnie 60s). Anti-enum behaviour
/// pozostaje: endpoint zawsze zwraca 204, ale email NIE jest wysłany przy throttle.
/// </summary>
public sealed class ForgotPasswordThrottlingIntegrationTests
{
  [Fact]
  public async Task Repeated_forgot_password_for_same_email_sends_only_one_email_in_cooldown_window()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Trzy żądania reset w szybkim ciągu z tym samym mailem
    for (var i = 0; i < 3; i++)
    {
      var response = await client.PostAsJsonAsync(
        "/api/auth/forgot-password",
        new { email = "owner@rest-seed.local" },
        ct);
      // Anti-enum: kontrakt 204 zachowany niezależnie od throttle
      Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    var resetsForOwner = mailbox.PasswordResetsSent
      .Where(s => string.Equals(s.ToEmail, "owner@rest-seed.local", StringComparison.OrdinalIgnoreCase))
      .ToList();

    // Throttling per-email: powinien być DOKŁADNIE 1 mail wysłany w oknie 60s
    Assert.Single(resetsForOwner);
  }

  [Fact]
  public async Task Forgot_password_for_different_emails_each_sends_its_own_reset()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var emails = new[] { "owner@rest-seed.local", "owner2@rest-seed.local" };
    foreach (var email in emails)
    {
      var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email }, ct);
      Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // Throttling per-email nie powinien blokować innych adresów
    foreach (var email in emails)
    {
      Assert.Contains(mailbox.PasswordResetsSent, s => string.Equals(s.ToEmail, email, StringComparison.OrdinalIgnoreCase));
    }
  }

  [Fact]
  public async Task Forgot_password_for_unknown_email_still_throttled_to_avoid_enumeration_via_send_count()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Unknown email — nie wysyła nic (bo user nie istnieje), ale zawsze 204
    for (var i = 0; i < 3; i++)
    {
      var resp = await client.PostAsJsonAsync(
        "/api/auth/forgot-password",
        new { email = "nobody-unknown@nowhere.local" },
        ct);
      Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    Assert.Empty(mailbox.PasswordResetsSent.Where(s => s.ToEmail.Contains("nobody-unknown", StringComparison.OrdinalIgnoreCase)));
  }
}
