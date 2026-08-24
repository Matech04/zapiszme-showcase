using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// VAT-CAT-003 — utworzenie salonu auto-seeduje polskie stawki VAT.
///
/// Droga B: seeding VAT przeniósł się z rejestracji do kroku kreatora tworzącego tenanta
/// (<c>POST /api/onboarding/profile</c> → <c>CompleteProfileCommand</c>). Testy przechodzą więc
/// pełny mini-flow (slim rejestracja → potwierdzenie → profil) zamiast zakładać, że tenant powstaje
/// z samej rejestracji.
///
/// Regresja: bez seedingu nowy właściciel zakładał usługi w panelu, formularz wymagał wyboru
/// VatRate, ale lista była pusta — wymuszony detour przez ekran tworzenia stawki. Te testy gwarantują,
/// że po ukończeniu kroku „nazwa salonu" właściciel ma już komplet PL VAT.
/// </summary>
public sealed class RegisterOwnerVatSeedingIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Onboarding_seeds_full_PL_vat_catalog_for_new_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (_, tenantId, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@vat-seed.local", "+48501111001", "VAT Seed Salon", "vat-seed-salon", ct: ct);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var vats = await db.VatRates
      .IgnoreQueryFilters()
      .Where(v => v.TenantId == tenantId)
      .AsNoTracking()
      .ToListAsync(ct);

    Assert.Equal(5, vats.Count);
    // Domyślną stawką dla nowego tenanta (kosmetyka SOLO) jest „zw." (zwolnienie art. 113),
    // nie 23% — zgodnie z VatRateCatalog. 23% pozostaje w katalogu, ale bez flagi IsDefault.
    Assert.Contains(vats, v => v.Name == "zw." && v.Value == 0.00m && v.IsDefault);
    Assert.Contains(vats, v => v.Name == "23%" && v.Value == 0.23m && !v.IsDefault);
    Assert.Contains(vats, v => v.Name == "8%" && v.Value == 0.08m);
    Assert.Contains(vats, v => v.Name == "5%" && v.Value == 0.05m);
    Assert.Contains(vats, v => v.Name == "0%" && v.Value == 0.00m);
    Assert.Single(vats, v => v.IsDefault);
  }

  [Fact]
  public async Task Two_owners_get_isolated_vat_catalogs()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (_, firstTenantId, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner-first@vat-seed.local", "+48501111002", "First VAT Salon", "first-vat-salon", ct: ct);
    var (_, secondTenantId, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner-second@vat-seed.local", "+48501111003", "Second VAT Salon", "second-vat-salon", ct: ct);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var firstVats = await db.VatRates.IgnoreQueryFilters()
      .Where(v => v.TenantId == firstTenantId).CountAsync(ct);
    var secondVats = await db.VatRates.IgnoreQueryFilters()
      .Where(v => v.TenantId == secondTenantId).CountAsync(ct);

    Assert.Equal(5, firstVats);
    Assert.Equal(5, secondVats);
  }

  // Po ukończeniu kroku „nazwa salonu" właściciel od razu widzi pełną listę przez publiczne API panelu —
  // żeby formularz „nowa usługa" miał z czego wybrać. Ten sam uwierzytelniony klient (nagłówki testowego
  // schematu), którym utworzyliśmy tenanta, odpytuje /api/VatRates.
  [Fact]
  public async Task Onboarded_owner_sees_seeded_VAT_rates_via_API()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@visible-vat.local", "+48501111004", "Visible VAT Salon", "visible-vat-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var listResponse = await ownerClient.GetAsync("/api/VatRates", ct);

    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    var rates = await listResponse.Content.ReadFromJsonAsync<List<VatRateRead>>(JsonRead, ct);
    Assert.NotNull(rates);
    Assert.Equal(5, rates!.Count);
  }

  private sealed record VatRateRead(Guid Id, string Name, decimal Value, bool IsDefault);
}
