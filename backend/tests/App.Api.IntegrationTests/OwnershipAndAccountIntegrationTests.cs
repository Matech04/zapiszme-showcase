using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Faza 2 — przekazanie konta właściciela (transfer admina) oraz self-service konta
/// (zmiana własnej nazwy / hasła / e-maila). Hasło seed-użytkowników = "Password123!".
/// </summary>
public sealed class OwnershipAndAccountIntegrationTests
{
  private const string SeedPassword = "Password123!";

  [Fact]
  public async Task Admin_can_transfer_ownership_changes_login_email_and_sends_reset()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var admin = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await admin.PostAsJsonAsync(
      "/api/auth/admin/transfer-ownership",
      new { tenantId = seed.TenantId, newEmail = "new-owner@handover.local" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var ownerUser = db.Users.Single(u => u.Id == IntegrationTestUserIds.SalonOwner);
    Assert.Equal("new-owner@handover.local", ownerUser.Email);
    Assert.Equal("new-owner@handover.local", ownerUser.UserName);

    var ownerEmployee = db.Employees.IgnoreQueryFilters().Single(e => e.Id == seed.EmployeeId);
    Assert.Equal("new-owner@handover.local", ownerEmployee.Email);

    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastPasswordResetUrl);
    Assert.Contains(IntegrationTestUserIds.SalonOwner.ToString(), mailbox.LastPasswordResetUrl!);
  }

  [Fact]
  public async Task Transfer_ownership_requires_system_admin_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      "/api/auth/admin/transfer-ownership",
      new { tenantId = seed.TenantId, newEmail = "nope@handover.local" },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Owner_can_change_own_password()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      "/api/auth/account/password",
      new { currentPassword = SeedPassword, newPassword = "BrandNewPass123!" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task Change_password_with_wrong_current_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      "/api/auth/account/password",
      new { currentPassword = "WrongPassword!", newPassword = "BrandNewPass123!" },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Owner_changes_email_only_after_confirming_link()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Krok 1: request — wysyła link, ale NIE zmienia jeszcze e-maila.
    var request = await owner.PostAsJsonAsync(
      "/api/auth/account/email",
      new { currentPassword = SeedPassword, newEmail = "owner-self@rest-seed.local" },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var user = db.Users.Single(u => u.Id == IntegrationTestUserIds.SalonOwner);
      Assert.Equal("owner@rest-seed.local", user.Email); // wciąż stary
    }

    // Link poszedł na NOWY adres.
    var mailbox = factory.Services.GetRequiredService<TestAuthEmailMailbox>();
    Assert.NotNull(mailbox.LastChangeEmailConfirmationUrl);
    var query = new Uri(mailbox.LastChangeEmailConfirmationUrl!).Query.TrimStart('?')
      .Split('&').ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));

    // Krok 2: potwierdzenie (anonimowo, z linku).
    var anon = factory.CreateClient();
    var confirm = await anon.PostAsJsonAsync(
      "/api/auth/account/confirm-change-email",
      new { userId = query["userId"], token = query["token"], email = query["email"] },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var user = db.Users.Single(u => u.Id == IntegrationTestUserIds.SalonOwner);
      Assert.Equal("owner-self@rest-seed.local", user.Email);
      Assert.Equal("owner-self@rest-seed.local", user.UserName);
      var employee = db.Employees.IgnoreQueryFilters().Single(e => e.Id == seed.EmployeeId);
      Assert.Equal("owner-self@rest-seed.local", employee.Email);
    }
  }

  [Fact]
  public async Task Confirm_change_email_with_bad_token_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var anon = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var confirm = await anon.PostAsJsonAsync(
      "/api/auth/account/confirm-change-email",
      new
      {
        userId = IntegrationTestUserIds.SalonOwner,
        token = "aW52YWxpZC10b2tlbg", // „invalid-token" base64url
        email = "hacker@rest-seed.local",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
  }

  [Fact]
  public async Task Change_email_with_wrong_password_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      "/api/auth/account/email",
      new { currentPassword = "WrongPassword!", newEmail = "should-not@rest-seed.local" },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Owner_can_update_display_name()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await owner.PostAsJsonAsync(
      "/api/auth/account/profile",
      new { displayName = "Nowa Nazwa" },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = db.Users.Single(u => u.Id == IntegrationTestUserIds.SalonOwner);
    Assert.Equal("Nowa Nazwa", user.DisplayName);
  }
}
