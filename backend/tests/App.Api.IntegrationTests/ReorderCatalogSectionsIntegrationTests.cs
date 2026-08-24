using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// SVC-REORDER-SECTIONS — unified-reorder katalogu (PUT /api/ServiceCategories/reorder-sections):
/// realne kategorie + wirtualna sekcja „Bez kategorii" (null) zapisane w jednej sekwencji.
/// Real Postgres (Testcontainers) — weryfikuje routing, autoryzację StaffManagement, trwałość
/// uncategorizedOrderIndex na Tenancie i OrderIndex kategorii. Po reorderze z null na pozycji 1
/// GET uncategorized-order zwraca 1, a kategorie mają indeksy wg pozycji na liście.
/// </summary>
public sealed class ReorderCatalogSectionsIntegrationTests
{
  [Fact]
  public async Task Reorder_with_null_at_position_1_persists_uncategorized_and_category_indices()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Druga kategoria, by sekwencja [cat, null, cat] miała sens.
    var createResp = await client.PostAsJsonAsync(
      "/api/ServiceCategories",
      new { name = "Druga kategoria", orderIndex = 1 },
      ct);
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
    var secondCategoryId = JsonDocument
      .Parse(await createResp.Content.ReadAsStringAsync(ct))
      .RootElement.GetGuid();

    // Docelowa kolejność: [seedowa kategoria, „Bez kategorii" (null), druga kategoria].
    var orderedSections = new Guid?[] { seed.ServiceCategoryId, null, secondCategoryId };
    var reorderResp = await client.PutAsJsonAsync(
      "/api/ServiceCategories/reorder-sections",
      new { orderedSections },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, reorderResp.StatusCode);

    // Sekcja „Bez kategorii" wylądowała na pozycji 1.
    var uncategorizedResp = await client.GetAsync("/api/ServiceCategories/uncategorized-order", ct);
    Assert.Equal(HttpStatusCode.OK, uncategorizedResp.StatusCode);
    using var uncDoc = JsonDocument.Parse(await uncategorizedResp.Content.ReadAsStringAsync(ct));
    Assert.Equal(1, uncDoc.RootElement.GetProperty("orderIndex").GetInt32());

    // Kategorie mają OrderIndex wg pozycji na liście: seedowa=0, druga=2.
    var categoriesResp = await client.GetAsync("/api/ServiceCategories", ct);
    using var catDoc = JsonDocument.Parse(await categoriesResp.Content.ReadAsStringAsync(ct));
    var byId = catDoc.RootElement.EnumerateArray()
      .ToDictionary(e => e.GetProperty("id").GetGuid(), e => e.GetProperty("orderIndex").GetInt32());

    Assert.Equal(0, byId[seed.ServiceCategoryId]);
    Assert.Equal(2, byId[secondCategoryId]);
  }
}
