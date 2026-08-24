using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Wspólne kroki „Drogi B" (slim rejestracja → potwierdzenie → kreator) dla testów integracyjnych.
///
/// Rejestracja właściciela NIE tworzy już tenanta — Tenant/Employee/VAT/promo powstają dopiero w
/// <c>POST /api/onboarding/profile</c>. Testy, które potrzebują „właściciela z tenantem", przechodzą
/// przez ten helper zamiast zakładać, że tenant istnieje po samej rejestracji.
///
/// Potwierdzenie e-maila/telefonu robimy przez bezpośrednie ustawienie flag w DB (jest już na to
/// precedens — patrz ResendConfirmEmailIntegrationTests). Właściwy flow confirm-email/confirm-phone
/// testuje RegisterOwnerConfirmationFlowIntegrationTests; tutaj interesuje nas krok tworzenia tenanta,
/// a nie ponowne testowanie potwierdzeń. Dzięki temu rejestracja jest JEDYNYM anonimowym żądaniem,
/// więc nie wpadamy w globalny per-IP limit (Global=3/60s) — kroki kreatora idą już jako uwierzytelnione
/// (nagłówki testowego schematu → partycja "user:" z osobnym budżetem).
/// </summary>
internal static class OnboardingTestSupport
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  internal sealed record RegisterOwnerResponseBody(Guid UserId, string Email, bool EmailConfirmed);

  internal sealed record CompleteProfileResponseBody(Guid TenantId, Guid EmployeeId);

  /// <summary>Slim rejestracja właściciela. Zwraca userId. Domyślnie oczekuje 200.</summary>
  public static async Task<Guid> RegisterOwnerAsync(
    HttpClient client,
    string email,
    string phoneNumber,
    string? promoCode = null,
    CancellationToken ct = default)
  {
    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email,
        password = "Password123!",
        phoneNumber,
        turnstileToken = (string?)null,
        promoCode,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);
    Assert.NotNull(body);
    return body!.UserId;
  }

  /// <summary>
  /// Ustawia EmailConfirmed=true i PhoneNumberConfirmed=true bezpośrednio w DB — symulacja stanu
  /// po przejściu confirm-email + confirm-phone, bez odpalania anonimowych endpointów (oszczędza
  /// per-IP budżet i nie testuje ponownie tego, co i tak pokrywa RegisterOwnerConfirmationFlow).
  /// </summary>
  public static async Task ConfirmAccountInDbAsync(
    BookingApiApplicationFactory factory,
    Guid userId,
    CancellationToken ct = default)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
    user.EmailConfirmed = true;
    user.PhoneNumberConfirmed = true;
    await db.SaveChangesAsync(ct);
  }

  /// <summary>
  /// Zakłada w DB konto admina systemowego: User + rola Admin, BEZ rekordu Employee — dokładnie tak,
  /// jak robią to <c>DbSeeder</c> i <c>EnsureProductionBootstrapAsync</c> (admin nigdy nie jest
  /// pracownikiem żadnego salonu).
  ///
  /// Sam nagłówek <c>Roles=Admin</c> tu nie wystarcza: testowy schemat auth fabrykuje tożsamość
  /// z nagłówków, a <c>GetOnboardingStateQuery</c> czyta rolę z bazy przez UserManager. Fabryka
  /// testowa nie odpala seeda panelu, więc konto musimy założyć jawnie.
  /// </summary>
  public static async Task<Guid> CreateSystemAdminInDbAsync(
    BookingApiApplicationFactory factory,
    string email = "admin@onboarding-state.local",
    CancellationToken ct = default)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "ADMIN", ct);
    if (adminRole == null)
    {
      adminRole = new IdentityRole<Guid>
      {
        Id = Guid.NewGuid(),
        Name = Roles.Admin,
        NormalizedName = "ADMIN",
        ConcurrencyStamp = Guid.NewGuid().ToString(),
      };
      db.Roles.Add(adminRole);
      await db.SaveChangesAsync(ct);
    }

    var user = new User(email, "System Admin")
    {
      Id = Guid.NewGuid(),
      EmailConfirmed = true,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    db.Users.Add(user);
    db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole.Id });
    await db.SaveChangesAsync(ct);

    return user.Id;
  }

  /// <summary>Klient uwierzytelniony jako podany user (schemat testowy: nagłówki UserId + Roles).</summary>
  public static HttpClient CreateUserClient(
    BookingApiApplicationFactory factory,
    Guid userId,
    string roles = "Owner")
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.UserId, userId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, roles);
    return client;
  }

  /// <summary>Surowe wywołanie kroku kreatora tworzącego tenanta (do asercji kodu statusu).</summary>
  public static Task<HttpResponseMessage> PostProfileAsync(
    HttpClient authedClient,
    string salonName,
    string salonSlug,
    string firstName = "Test",
    string lastName = "Owner",
    CancellationToken ct = default)
    => authedClient.PostAsJsonAsync(
      "/api/onboarding/profile",
      new { firstName, lastName, salonName, salonSlug },
      ct);

  /// <summary>
  /// Pełny happy-path: slim rejestracja → potwierdzenie w DB → krok kreatora tworzący tenanta.
  /// Zwraca (userId, tenantId, employeeId).
  /// </summary>
  public static async Task<(Guid UserId, Guid TenantId, Guid EmployeeId)> RegisterConfirmAndCreateTenantAsync(
    BookingApiApplicationFactory factory,
    string email,
    string phoneNumber,
    string salonName,
    string salonSlug,
    string? promoCode = null,
    string firstName = "Test",
    string lastName = "Owner",
    CancellationToken ct = default)
  {
    var anonClient = factory.CreateClient();
    var userId = await RegisterOwnerAsync(anonClient, email, phoneNumber, promoCode, ct);

    await ConfirmAccountInDbAsync(factory, userId, ct);

    var ownerClient = CreateUserClient(factory, userId);
    var profileResp = await PostProfileAsync(ownerClient, salonName, salonSlug, firstName, lastName, ct);
    Assert.Equal(HttpStatusCode.OK, profileResp.StatusCode);
    var body = await profileResp.Content.ReadFromJsonAsync<CompleteProfileResponseBody>(JsonRead, ct);
    Assert.NotNull(body);
    return (userId, body!.TenantId, body.EmployeeId);
  }
}
