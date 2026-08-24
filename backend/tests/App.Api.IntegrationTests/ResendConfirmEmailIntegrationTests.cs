using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// REG-RESEND-001..003 — POST /api/auth/resend-confirm-email.
///
/// Anti-enumeration: endpoint ZAWSZE zwraca 204, niezależnie od stanu konta. Czy faktyczny
/// mail poszedł, sprawdzamy bocznie przez TestAuthEmailMailbox (zlicza wysyłki).
/// </summary>
public sealed class ResendConfirmEmailIntegrationTests
{
  [Fact]
  public async Task Resend_for_unconfirmed_user_sends_new_confirm_email_link()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Zarejestruj — to wysyła PIERWSZY mail (zapamiętany w mailboxie jako LastConfirmEmailUrl).
    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "resend@e.local",
        password = "Password123!",
        phoneNumber = "+48501111050",
      },
      ct);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.Single(mailbox.ConfirmEmailsSent);

    var resendResp = await client.PostAsJsonAsync(
      "/api/auth/resend-confirm-email",
      new { email = "resend@e.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, resendResp.StatusCode);
    // Drugi mail trafił do mailboxa.
    Assert.Equal(2, mailbox.ConfirmEmailsSent.Count);
    Assert.All(mailbox.ConfirmEmailsSent, m => Assert.Equal("resend@e.local", m.ToEmail));
  }

  [Fact]
  public async Task Resend_for_already_confirmed_user_does_not_send_anything_but_returns_204()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "already@e.local",
        password = "Password123!",
        phoneNumber = "+48501111051",
      },
      ct);

    // Wymuszam EmailConfirmed=true w DB (omijam wysyłkę confirm-email z linkiem).
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var user = await db.Users.FirstAsync(u => u.Email == "already@e.local", ct);
      user.EmailConfirmed = true;
      await db.SaveChangesAsync(ct);
    }

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var sendsBefore = mailbox.ConfirmEmailsSent.Count;

    var resendResp = await client.PostAsJsonAsync(
      "/api/auth/resend-confirm-email",
      new { email = "already@e.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, resendResp.StatusCode);
    // Brak nowego maila — user jest już potwierdzony, więc handler skipuje wysyłkę.
    Assert.Equal(sendsBefore, mailbox.ConfirmEmailsSent.Count);
  }

  // Per-email throttle: drugi resend pod rząd, dla tego samego adresu, w ciągu 60s
  // jest cicho dropowany (204 anti-enum, ale ConfirmEmailsSent nie rośnie). Chroni ofiarę
  // przed botnetem rozproszonym po wielu IP — per-IP AuthSensitive tego nie pokrywa.
  [Fact]
  public async Task Resend_twice_in_a_row_throttles_second_send_per_email()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "throttle@e.local",
        password = "Password123!",
        phoneNumber = "+48501111052",
      },
      ct);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var sendsAfterRegister = mailbox.ConfirmEmailsSent.Count;

    var firstResend = await client.PostAsJsonAsync(
      "/api/auth/resend-confirm-email",
      new { email = "throttle@e.local" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, firstResend.StatusCode);
    Assert.Equal(sendsAfterRegister + 1, mailbox.ConfirmEmailsSent.Count);

    var secondResend = await client.PostAsJsonAsync(
      "/api/auth/resend-confirm-email",
      new { email = "throttle@e.local" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, secondResend.StatusCode);
    // Throttle ucisza drugą wysyłkę — licznik bez zmian.
    Assert.Equal(sendsAfterRegister + 1, mailbox.ConfirmEmailsSent.Count);
  }

  // Anti-enum: nieznany email też 204 (nie da się wycyrklować bazy userów). Walidacja
  // formatu emaila zostaje — pusty/zniekształcony email wciąż leci jako 400 z ASP.NET.
  [Fact]
  public async Task Resend_for_unknown_email_returns_204_without_sending()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    var sendsBefore = mailbox.ConfirmEmailsSent.Count;

    var response = await client.PostAsJsonAsync(
      "/api/auth/resend-confirm-email",
      new { email = "no-such-user@nope.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(sendsBefore, mailbox.ConfirmEmailsSent.Count);
  }
}
