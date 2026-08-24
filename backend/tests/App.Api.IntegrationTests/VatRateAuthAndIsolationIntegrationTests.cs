using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// VAT-006/007/008 — izolacja cross-tenant i autoryzacja StaffManagement dla zapisów.
/// </summary>
public sealed class VatRateAuthAndIsolationIntegrationTests
{
  // VAT-008 Authorization: POST → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_create_vat_rate_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/VatRates",
      new { name = "VAT 8%", value = 0.08, isDefault = false },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // VAT-008 Authorization: PUT → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_update_vat_rate_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync(
      $"/api/VatRates/{seed.VatRateId}",
      new { id = seed.VatRateId, name = "Hacked", value = 0.10, isDefault = false },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // VAT-008 Authorization: DELETE → 403 dla Employee
  [Fact]
  public async Task Employee_role_cannot_delete_vat_rate_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.DeleteAsync($"/api/VatRates/{seed.VatRateId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // VAT-007: GET /{id} dla cross-tenant → 404
  [Fact]
  public async Task Owner_cannot_get_vat_rate_from_other_tenant_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync($"/api/VatRates/{second.VatRateId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // VAT-006 Security: GET /api/VatRates zwraca tylko własny tenant
  [Fact]
  public async Task Get_vat_rates_returns_only_own_tenant_records()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync("/api/VatRates", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<VatRateListItem>>(cancellationToken: ct);
    Assert.NotNull(list);
    Assert.Contains(list, v => v.Id == own.VatRateId);
    Assert.DoesNotContain(list, v => v.Id == second.VatRateId);
  }

  // VAT-003 EdgeCase: DELETE wykonuje soft-delete (rekord zostaje, IsActive=false)
  [Fact]
  public async Task Delete_vat_rate_performs_soft_delete()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var deleteResponse = await ownerClient.DeleteAsync($"/api/VatRates/{seed.VatRateId}", ct);
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<App.Infrastructure.Persistence.ApplicationDbContext>();
    var soft = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
      Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.VatRates),
      v => v.Id == seed.VatRateId, ct);
    Assert.False(soft.IsActive);
  }

  private sealed record VatRateListItem(Guid Id);
}
