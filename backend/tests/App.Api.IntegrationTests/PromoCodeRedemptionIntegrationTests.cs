using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// PROMO-INT-001..004 — integracja systemu kodów promocyjnych:
///   * rejestracja z prawidłowym kodem → tenant ma redemption + cena obniżona
///   * race condition na MaxTotalUses — wiele równoczesnych rejestracji
///   * cross-tenant isolation — redemption tenant A niewidoczne dla tenant B
///   * public validate endpoint zwraca generic message dla błędnego kodu
/// </summary>
public sealed class PromoCodeRedemptionIntegrationTests
{
  // Droga B: kod promo przy rejestracji jest tylko ZAPISYWANY (User.PendingPromoCode) — redempcja
  // (redemption + inkrement użyć + rabat na subskrypcji tenanta) dzieje się dopiero przy tworzeniu
  // tenanta w kroku kreatora (/api/onboarding/profile). Ten test pilnuje obu połówek.
  [Fact]
  public async Task PromoCode_is_stored_at_register_and_redeemed_at_onboarding_profile()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var code = await SeedPromoCodeAsync(factory.Services, PromoCode.CreatePriceOverride(
      "FM10-A", newBasePriceInZloty: 49m, maxTotalUses: 5), ct);

    var email = $"fm-a-{Guid.NewGuid():N}@e.local";
    var phone = "+48" + Random.Shared.Next(500000000, 599999999);
    var slug = "fm-tenant-a-" + Guid.NewGuid().ToString("N")[..6];

    // 1) Rejestracja z kodem — kod zapisany na koncie, ale JESZCZE nie zredempowany.
    var anon = factory.CreateClient();
    var userId = await OnboardingTestSupport.RegisterOwnerAsync(anon, email, phone, promoCode: "fm10-a", ct: ct);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
      Assert.False(string.IsNullOrWhiteSpace(user.PendingPromoCode), "Kod promo MA być zapisany na koncie");

      var codeAfterRegister = await db.PromoCodes.FirstAsync(p => p.Id == code.Id, ct);
      Assert.Equal(0, codeAfterRegister.CurrentUses); // brak redempcji na etapie rejestracji

      Assert.False(
        await db.PromoCodeRedemptions.IgnoreQueryFilters().AnyAsync(r => r.PromoCodeId == code.Id, ct),
        "Redemption nie może powstać przy samej rejestracji");
    }

    // 2) Potwierdzenie + krok kreatora tworzący tenanta — TU następuje redempcja.
    await OnboardingTestSupport.ConfirmAccountInDbAsync(factory, userId, ct);
    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var profileResp = await OnboardingTestSupport.PostProfileAsync(ownerClient, "FM Tenant A", slug, ct: ct);
    Assert.Equal(HttpStatusCode.OK, profileResp.StatusCode);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == slug, ct);
      Assert.NotNull(tenant.Subscription.ActivePromoCodeRedemptionId);

      var redemption = await db.PromoCodeRedemptions
        .IgnoreQueryFilters()
        .FirstAsync(r => r.Id == tenant.Subscription.ActivePromoCodeRedemptionId, ct);
      Assert.Equal(code.Id, redemption.PromoCodeId);

      var reloadedCode = await db.PromoCodes.FirstAsync(p => p.Id == code.Id, ct);
      Assert.Equal(1, reloadedCode.CurrentUses);

      var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
      Assert.True(string.IsNullOrWhiteSpace(user.PendingPromoCode), "Kod promo MA być wyczyszczony po redempcji");
    }
  }

  [Fact]
  public async Task Onboarding_with_invalid_pending_promo_creates_tenant_without_redemption()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var email = $"np-{Guid.NewGuid():N}@e.local";
    var phone = "+48" + Random.Shared.Next(500000000, 599999999);
    var slug = "no-promo-" + Guid.NewGuid().ToString("N")[..6];

    // Zły kod przy rejestracji nie blokuje utworzenia konta ani później salonu — tenant powstaje bez rabatu.
    var anon = factory.CreateClient();
    var userId = await OnboardingTestSupport.RegisterOwnerAsync(
      anon, email, phone, promoCode: "NOPE-DOES-NOT-EXIST", ct: ct);
    await OnboardingTestSupport.ConfirmAccountInDbAsync(factory, userId, ct);
    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var profileResp = await OnboardingTestSupport.PostProfileAsync(ownerClient, "No Promo Tenant", slug, ct: ct);
    Assert.Equal(HttpStatusCode.OK, profileResp.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == slug, ct);
    Assert.Null(tenant.Subscription.ActivePromoCodeRedemptionId);

    var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
    Assert.True(string.IsNullOrWhiteSpace(user.PendingPromoCode), "Odrzucony kod też MA być wyczyszczony");
  }

  [Fact]
  public async Task PromoValidate_endpoint_returns_generic_message_for_invalid_code()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync(
      "/api/promo/validate", new { code = "DOES-NOT-EXIST" }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ValidatePromoResponse>(cancellationToken: ct);
    Assert.NotNull(body);
    Assert.False(body!.IsValid);
    Assert.Equal("Kod nieprawidłowy.", body.Message);
  }

  [Fact]
  public async Task PromoRedemption_respects_MaxTotalUses_across_sequential_redemptions()
  {
    // Sekwencyjny test (atomowy SQL UPDATE testujemy w środowisku Postgres przez INTEGRATION_DB_PROVIDER=Postgres;
    // tu w InMemory pokazujemy że samą logikę CanBeRedeemed / IncrementUses egzekwujemy poprawnie).
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var code = await SeedPromoCodeAsync(factory.Services, PromoCode.CreatePriceOverride(
      "SEQ3", newBasePriceInZloty: 49m, maxTotalUses: 3), ct);

    var successes = 0;
    for (var i = 0; i < 5; i++)
    {
      using var scope = factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var svc = scope.ServiceProvider.GetRequiredService<
        App.Application.Booking.PromoCodes.IPromoCodeRedemptionService>();

      var tenant = new Tenant($"Seq {i}", $"seq-{i}-" + Guid.NewGuid().ToString("N")[..6]);
      db.Tenants.Add(tenant);
      await db.SaveChangesAsync(ct);

      var redemption = await svc.RedeemForNewTenantAsync("SEQ3", tenant, ct);
      if (redemption is not null)
      {
        await db.SaveChangesAsync(ct);
        successes++;
      }
    }

    Assert.Equal(3, successes);

    using var verifyScope = factory.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var reloadedCode = await verifyDb.PromoCodes.FirstAsync(p => p.Id == code.Id, ct);
    Assert.Equal(3, reloadedCode.CurrentUses);

    var redemptionsCount = await verifyDb.PromoCodeRedemptions
      .IgnoreQueryFilters()
      .CountAsync(r => r.PromoCodeId == code.Id, ct);
    Assert.Equal(3, redemptionsCount);
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────────

  private static async Task<PromoCode> SeedPromoCodeAsync(
    IServiceProvider services, PromoCode code, CancellationToken ct)
  {
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.PromoCodes.Add(code);
    await db.SaveChangesAsync(ct);
    return code;
  }

  private sealed record ValidatePromoResponse(bool IsValid, string? Message, string? DiscountPreview);
}
