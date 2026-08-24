using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.ServiceAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// SVC-021 — reorder usług/kategorii (PUT .../reorder) + HidePrice round-trip przez API.
/// Real Postgres (Testcontainers) — weryfikuje routing, autoryzację StaffManagement,
/// migrację (kolumny hide_price/order_index istnieją) oraz utrwalanie OrderIndex/HidePrice.
/// </summary>
public sealed class ServiceReorderAndHidePriceIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public async Task Create_service_with_HidePrice_persists_and_GET_returns_it()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var createResponse = await client.PostAsJsonAsync("/api/Services", new
    {
      categoryId = seed.ServiceCategoryId,
      vatRateId = seed.VatRateId,
      name = "Konsultacja",
      amount = 0m, // cena 0 nadal dozwolona
      currency = "PLN",
      durationInMinutes = 15,
      hidePrice = true,
    }, ct);

    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
    var newId = await createResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions, ct);

    var getResponse = await client.GetAsync($"/api/Services/{newId}", ct);
    using var doc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(ct));
    Assert.True(doc.RootElement.GetProperty("hidePrice").GetBoolean());
    Assert.Equal(0m, doc.RootElement.GetProperty("price").GetProperty("amount").GetDecimal());
  }

  [Fact]
  public async Task Reorder_services_persists_OrderIndex_and_list_is_sorted()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var b = await CreateService(client, seed, "Beta", ct);
    var c = await CreateService(client, seed, "Gamma", ct);
    var a = seed.ServiceId; // "Strzyżenie"

    // Docelowa kolejność: c, a, b.
    var reorderResponse = await client.PutAsJsonAsync("/api/Services/reorder", new
    {
      orderedServiceIds = new[] { c, a, b },
      categoryId = seed.ServiceCategoryId,
    }, ct);
    Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.Equal(0, (await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == c, ct)).OrderIndex);
    Assert.Equal(1, (await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == a, ct)).OrderIndex);
    Assert.Equal(2, (await db.Services.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == b, ct)).OrderIndex);

    // Listing zwraca posortowane wg OrderIndex.
    var listResponse = await client.GetAsync("/api/Services", ct);
    using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
    var orderedIds = listDoc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    Assert.Equal(new[] { c, a, b }, orderedIds);
  }

  [Fact]
  public async Task Reorder_services_with_cross_tenant_id_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var otherTenant = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync("/api/Services/reorder", new
    {
      orderedServiceIds = new[] { seed.ServiceId, otherTenant.ServiceId },
      categoryId = (Guid?)null,
    }, ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Reorder_categories_persists_OrderIndex_preserving_name()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var secondCategoryId = SeedCategory(factory.Services, seed.TenantId, "Druga");
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PutAsJsonAsync("/api/ServiceCategories/reorder", new
    {
      orderedCategoryIds = new[] { secondCategoryId, seed.ServiceCategoryId },
    }, ct);
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var second = await db.ServiceCategories.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == secondCategoryId, ct);
    Assert.Equal(0, second.OrderIndex);
    Assert.Equal("Druga", second.Name);
    Assert.Equal(1, (await db.ServiceCategories.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == seed.ServiceCategoryId, ct)).OrderIndex);
  }

  private static async Task<Guid> CreateService(HttpClient client, RestApiIntegrationSeedResult seed, string name, CancellationToken ct)
  {
    var response = await client.PostAsJsonAsync("/api/Services", new
    {
      categoryId = seed.ServiceCategoryId,
      vatRateId = seed.VatRateId,
      name,
      amount = 50m,
      currency = "PLN",
      durationInMinutes = 30,
    }, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions, ct);
  }

  private static Guid SeedCategory(IServiceProvider rootServices, Guid tenantId, string name)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var category = new ServiceCategory(tenantId, name, 0);
    db.ServiceCategories.Add(category);
    db.SaveChanges();
    return category.Id;
  }
}
