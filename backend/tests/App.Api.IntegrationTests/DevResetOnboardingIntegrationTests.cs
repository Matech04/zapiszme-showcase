using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// DEV-RESET — <c>POST /api/_dev/reset-onboarding</c>: narzędzie do ręcznego testowania kreatora.
///
/// Rdzeń kontraktu: kasuje SALON, ale ZOSTAWIA konto właściciela. To nie jest szczegół —
/// <c>TenantPurgeService.PurgeCoreAsync</c> domyślnie usuwa też powiązane konta Identity, więc
/// naiwne wywołanie <c>PurgeAsync</c> skasowałoby zalogowanego usera i reset nie miałby sensu
/// (trzeba by rejestrować się od nowa, z OTP i limitami — czyli dokładnie to, co reset omija).
/// Endpoint przekazuje <c>deleteOrphanedUsers: false</c>; te testy to pinują.
///
/// Testy jadą w env Testing (fabryka), gdzie endpoint jest ZA bramką (404) — dlatego ścieżkę
/// „konto przeżywa purge" weryfikujemy przez sam serwis, a przez HTTP pinujemy bramkę.
/// </summary>
public sealed class DevResetOnboardingIntegrationTests
{
  // Bramka: poza Development endpoint nie istnieje. Fabryka stawia env Testing, więc 404.
  [Fact]
  public async Task Reset_endpoint_is_not_exposed_outside_development()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@dev-reset-gate.local", "+48501444001", "Gate Salon", "gate-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.PostAsync("/api/_dev/reset-onboarding", content: null, ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // Sedno resetu: salon znika, konto (z potwierdzonym e-mailem i telefonem) zostaje, więc
  // GET /api/onboarding/state znów mówi „Profile" i kreator da się przejść od nowa.
  [Fact]
  public async Task Purge_with_deleteOrphanedUsers_false_removes_tenant_but_keeps_the_account()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@dev-reset.local", "+48501444002", "Reset Salon", "reset-salon", ct: ct);

    using (var scope = factory.Services.CreateScope())
    {
      var purge = scope.ServiceProvider
        .GetRequiredService<App.Application.Common.Interfaces.ITenantPurgeService>();
      await purge.PurgeAsync(tenantId, ct, deleteOrphanedUsers: false);
    }

    using var verifyScope = factory.Services.CreateScope();
    var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Salon i pracownik zniknęli...
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == employeeId, ct));

    // ...ale konto zostało i NADAL jest potwierdzone — bez tego kolejny przebieg kreatora
    // wymagałby ponownego OTP, czyli reset nie oszczędzałby niczego.
    var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId, ct);
    Assert.NotNull(user);
    Assert.True(user!.EmailConfirmed);
    Assert.True(user.PhoneNumberConfirmed);

    // I stan onboardingu wrócił na start kreatora.
    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var stateResponse = await ownerClient.GetAsync("/api/onboarding/state", ct);
    Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
    var state = await stateResponse.Content.ReadFromJsonAsync<OnboardingStateProbe>(
      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    Assert.NotNull(state);
    Assert.False(state!.HasTenant);
    Assert.Equal("Profile", state.NextStep);
  }

  // Kontrola negatywna: domyślne zachowanie purge (hard-delete salonu z panelu admina / cleanup
  // demo) NIE MOŻE się zmienić — konto ma znikać razem z salonem.
  [Fact]
  public async Task Purge_default_still_deletes_the_orphaned_account()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@dev-reset-default.local", "+48501444003", "Default Salon", "default-salon", ct: ct);

    using (var scope = factory.Services.CreateScope())
    {
      var purge = scope.ServiceProvider
        .GetRequiredService<App.Application.Common.Interfaces.ITenantPurgeService>();
      await purge.PurgeAsync(tenantId, ct);
    }

    using var verifyScope = factory.Services.CreateScope();
    var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct));
  }

  private sealed record OnboardingStateProbe(bool HasTenant, string NextStep);
}
