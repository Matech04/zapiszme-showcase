using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.UserAggregate;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Bramka telefonu przy logowaniu wymaga potwierdzenia numeru TYLKO gdy numer jest podany.
/// Konta provisionowane przez admina/właściciela (AdminCreateSalon, zaproszenie pracownika) nie
/// mają numeru — muszą móc się zalogować, inaczej brak numeru = brak OTP = zakleszczenie.
/// </summary>
public sealed class LoginPhoneGateIntegrationTests
{
  [Fact]
  public async Task Login_succeeds_for_account_without_phone_number()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    await CreateUserAsync(factory, "nophone@gate.local", phone: null, phoneConfirmed: false);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "nophone@gate.local", password = "Password123!", turnstileToken = (string?)null },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Login_blocked_for_account_with_unconfirmed_phone()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    await CreateUserAsync(factory, "withphone@gate.local", phone: "+48500999123", phoneConfirmed: false);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/login",
      new { email = "withphone@gate.local", password = "Password123!", turnstileToken = (string?)null },
      ct);

    // PhoneNotConfirmedException → 401 (UI prowadzi do /confirm-phone).
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  private static async Task CreateUserAsync(
    BookingApiApplicationFactory factory, string email, string? phone, bool phoneConfirmed)
  {
    using var scope = factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var user = new User(email, email)
    {
      EmailConfirmed = true,
      PhoneNumber = phone,
      PhoneNumberConfirmed = phoneConfirmed,
    };
    var result = await userManager.CreateAsync(user, "Password123!");
    if (!result.Succeeded)
    {
      throw new InvalidOperationException(
        "Nie udało się utworzyć użytkownika testowego: " +
        string.Join(", ", result.Errors.Select(e => e.Description)));
    }
  }
}
