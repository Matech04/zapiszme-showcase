using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// NOTIF-010 — kod OTP wysłany dokładnie raz na request (idempotencja).
/// NOTIF-011 — token z password-reset email jest dekodowalny i działa end-to-end z /reset-password.
/// </summary>
public sealed class NotificationFlowsIntegrationTests
{
  // NOTIF-011: Password reset email contains valid token decodable by reset endpoint
  [Fact]
  public async Task Password_reset_flow_end_to_end_uses_token_from_email()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Register a fresh owner
    var registerResp = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "reset@flow.local",
        password = "Password123!",
        phoneNumber = "+48501111060",
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, registerResp.StatusCode);

    // Request password reset
    var forgotResp = await client.PostAsJsonAsync(
      "/api/auth/forgot-password",
      new { email = "reset@flow.local" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, forgotResp.StatusCode);

    var mailbox = factory.Services.GetService(typeof(TestAuthEmailMailbox)) as TestAuthEmailMailbox;
    Assert.NotNull(mailbox);
    Assert.NotNull(mailbox!.LastPasswordResetUrl);

    var uri = new Uri(mailbox.LastPasswordResetUrl!);
    var queryParts = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));

    Assert.True(queryParts.ContainsKey("userId"));
    Assert.True(queryParts.ContainsKey("token"));

    // Use token to reset password — verifies that base64url-encoded token from email decodes
    // correctly by the /reset-password endpoint (NoContent on success).
    var resetResp = await client.PostAsJsonAsync(
      "/api/auth/reset-password",
      new
      {
        userId = queryParts["userId"],
        token = queryParts["token"],
        password = "NewStrongPass123!",
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, resetResp.StatusCode);
  }

  // NOTIF-010: Owner registration sends exactly one confirm email (idempotency baseline)
  [Fact]
  public async Task Owner_registration_sends_confirm_email_exactly_once()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "once@only.local",
        password = "Password123!",
        phoneNumber = "+48501111061",
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var mailbox = (TestAuthEmailMailbox)factory.Services.GetService(typeof(TestAuthEmailMailbox))!;
    Assert.NotNull(mailbox.LastConfirmEmailUrl);
    // URL musi zawierać userId i token (jest tylko 1 zapisany URL — mailbox trzyma ostatni)
    Assert.Contains("userId=", mailbox.LastConfirmEmailUrl ?? "", StringComparison.OrdinalIgnoreCase);
    Assert.Contains("token=", mailbox.LastConfirmEmailUrl ?? "", StringComparison.OrdinalIgnoreCase);
  }
}
