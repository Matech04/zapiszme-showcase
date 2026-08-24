using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api;
using App.Api.E2eSupport;
using App.Application.Common.Security;
using App.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// REG-CONFIRM-001..003 — wymaganie potwierdzenia maila (i telefonu) po rejestracji.
///
/// Baseline `BookingApiApplicationFactory` ustawia środowisko "Testing", co Program.cs
/// wykorzystuje do wyłączenia RequireConfirmedEmail. Test "login bez potwierdzenia"
/// uruchamiamy w lokalnej fabryce, która jawnie włącza ten gate przez Configure&lt;IdentityOptions&gt;.
/// </summary>
public sealed class RegisterOwnerConfirmationFlowIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Register_owner_response_signals_emailConfirmed_false()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "owner@confirm.local",
        password = "Password123!",
        phoneNumber = "+48501111040",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);
    Assert.NotNull(body);
    Assert.False(body!.EmailConfirmed);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.FirstAsync(u => u.Id == body.UserId, ct);
    Assert.False(user.EmailConfirmed);
    Assert.False(user.PhoneNumberConfirmed);
    Assert.Equal("+48501111040", user.PhoneNumber);
  }

  [Fact]
  public async Task Login_before_email_confirmation_is_rejected_when_RequireConfirmedEmail_on()
  {
    // Nadbudowa na bazowej fabryce — ta jako jedyna tworzy sobie izolowaną bazę testową.
    // Własny `WebApplicationFactory<Program>` brał connection string z procesowej zmiennej
    // środowiskowej, czyli bazę cudzej fabryki, i tracił ją, gdy tamta robiła `DROP DATABASE`.
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(ConfigureRequireConfirmedEmail);
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "preconfirm@e.local",
        password = "Password123!",
        phoneNumber = "+48501111041",
      },
      ct);

    var loginResp = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "preconfirm@e.local", password = "Password123!", rememberMe = false },
      ct);

    Assert.NotEqual(HttpStatusCode.OK, loginResp.StatusCode);
  }

  [Fact]
  public async Task After_full_confirmation_login_succeeds()
  {
    // Używamy bazowej fabryki (Testing env, RequireConfirmedEmail=false) — gate na telefon
    // (PhoneNumberConfirmed) działa niezależnie od Identity options. Testing env ma
    // RateLimiting:Global:PermitLimit=3 — testy rate-limiterka tego wymagają, ale my mamy
    // 5+ wywołań w jednym teście, więc lokalnie podnosimy limit przez WithWebHostBuilder.
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:Global:PermitLimit", "100");
      builder.UseSetting("RateLimiting:Auth:PermitLimit", "100");
    });
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var registerResponse = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "postconfirm@e.local",
        password = "Password123!",
        phoneNumber = "+48501111042",
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastConfirmEmailUrl);
    var uri = new Uri(mailbox.LastConfirmEmailUrl!);
    var qs = uri.Query.TrimStart('?').Split('&')
      .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));

    var confirmEmailResp = await client.PostAsJsonAsync(
      "/api/auth/confirm-email",
      new { userId = qs["userId"], token = qs["token"] },
      ct);
    Assert.Equal(HttpStatusCode.OK, confirmEmailResp.StatusCode);

    var confirmEmailBody = await confirmEmailResp.Content.ReadFromJsonAsync<ConfirmEmailResponseBody>(JsonRead, ct);
    Assert.NotNull(confirmEmailBody);
    Assert.True(confirmEmailBody!.EmailConfirmed);
    Assert.True(confirmEmailBody.PhoneOtpSent);
    Assert.False(confirmEmailBody.PhoneNumberConfirmed);

    // Login dopóki telefon niepotwierdzony — 401 PhoneNotConfirmed.
    var loginRespBlocked = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "postconfirm@e.local", password = "Password123!", rememberMe = false },
      ct);
    Assert.Equal(HttpStatusCode.Unauthorized, loginRespBlocked.StatusCode);

    // Pobieramy kod z fake'a + potwierdzamy telefon.
    var smsMailbox = factory.Services.GetRequiredService<TestPhoneOtpMailbox>();
    var code = smsMailbox.LastCodeForPhone("+48501111042");
    Assert.NotNull(code);

    var confirmPhoneResp = await client.PostAsJsonAsync(
      "/api/auth/confirm-phone",
      new { userId = confirmEmailBody.UserId, code },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, confirmPhoneResp.StatusCode);

    var loginResp = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "postconfirm@e.local", password = "Password123!", rememberMe = false },
      ct);
    Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
  }

  [Fact]
  public async Task ConfirmPhone_before_email_returns_400()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var registerResponse = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "order@e.local",
        password = "Password123!",
        phoneNumber = "+48501111043",
      },
      ct);
    var body = await registerResponse.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);

    var resp = await client.PostAsJsonAsync(
      "/api/auth/confirm-phone",
      new { userId = body!.UserId, code = "123456" },
      ct);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task ResendPhoneOtp_before_email_returns_400()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var registerResponse = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "resend@e.local",
        password = "Password123!",
        phoneNumber = "+48501111044",
      },
      ct);
    var body = await registerResponse.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);

    var resp = await client.PostAsJsonAsync(
      "/api/auth/resend-phone-otp",
      new { userId = body!.UserId },
      ct);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Register_with_duplicate_phone_returns_409()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "first-phone@e.local",
        password = "Password123!",
        phoneNumber = "+48501111045",
      },
      ct);

    var second = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "second-phone@e.local",
        password = "Password123!",
        phoneNumber = "+48501111045",
      },
      ct);

    Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
  }

  private sealed record RegisterOwnerResponseBody(Guid UserId, string Email, bool EmailConfirmed);

  private sealed record ConfirmEmailResponseBody(
    Guid UserId,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    bool PhoneOtpSent,
    string? MaskedPhone);

  /// <summary>
  /// Wymusza <c>RequireConfirmedEmail = true</c>, żeby przetestować ścieżkę
  /// <c>PasswordSignInAsync</c> odrzucającą logowanie przed potwierdzeniem e-maila.
  ///
  /// Nakładka na `BookingApiApplicationFactory`, a nie osobny <c>WebApplicationFactory</c>:
  /// tylko bazowa fabryka tworzy izolowaną bazę testową. Własna brała connection string
  /// z procesowej zmiennej środowiskowej — czyli bazę cudzej fabryki — i traciła ją, gdy tamta
  /// kończyła i robiła `DROP DATABASE`.
  /// </summary>
  private static void ConfigureRequireConfirmedEmail(IWebHostBuilder builder)
  {
    builder.ConfigureAppConfiguration((_, config) =>
    {
      // Test pełnego flow wykonuje wiele wywołań auth w szybkim tempie — podnosimy limit
      // żeby nie wpadał w AuthSensitive 20/min (testserver dzieli partycję "ip:unknown").
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RateLimiting:Auth:PermitLimit"] = "200",
      });
    });
    builder.ConfigureTestServices(services =>
    {
      services.Configure<IdentityOptions>(o => o.SignIn.RequireConfirmedEmail = true);

      foreach (var d in services.Where(x => x.ServiceType == typeof(IAuthEmailSender)).ToList())
      {
        services.Remove(d);
      }
      services.AddSingleton<TestAuthEmailMailbox>();
      services.AddSingleton<IAuthEmailSender>(sp => sp.GetRequiredService<TestAuthEmailMailbox>());

      foreach (var d in services.Where(x => x.ServiceType == typeof(IPhoneOtpSender)).ToList())
      {
        services.Remove(d);
      }
      services.AddSingleton<TestPhoneOtpMailbox>();
      services.AddSingleton<IPhoneOtpSender>(sp => sp.GetRequiredService<TestPhoneOtpMailbox>());
    });
  }
}
