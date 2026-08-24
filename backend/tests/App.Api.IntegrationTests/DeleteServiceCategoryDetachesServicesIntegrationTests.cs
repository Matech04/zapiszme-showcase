using System.Net;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// SVC-022 — usunięcie kategorii (DELETE /api/ServiceCategories/{id}) odpina jej usługi
/// do „Bez kategorii": usługa zostaje aktywna z categoryId == null (orphan).
/// Real Postgres (Testcontainers) — weryfikuje routing, autoryzację StaffManagement
/// oraz transakcyjne odpięcie usług razem z deaktywacją kategorii.
/// Regresja na bug ghost-services (usługi „zawisłe" pod nieaktywną kategorią).
/// </summary>
public sealed class DeleteServiceCategoryDetachesServicesIntegrationTests
{
  [Fact]
  public async Task Delete_category_detaches_its_service_to_orphan_and_keeps_it_active()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Sanity: seedowana usługa należy do seedowanej kategorii.
    var beforeResponse = await client.GetAsync($"/api/Services/{seed.ServiceId}", ct);
    using (var beforeDoc = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync(ct)))
    {
      Assert.Equal(seed.ServiceCategoryId, beforeDoc.RootElement.GetProperty("categoryId").GetGuid());
    }

    var deleteResponse = await client.DeleteAsync($"/api/ServiceCategories/{seed.ServiceCategoryId}", ct);
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    // Usługa nadal istnieje, jest aktywna i stała się orphanem (categoryId == null).
    var afterResponse = await client.GetAsync($"/api/Services/{seed.ServiceId}", ct);
    Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
    using var afterDoc = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync(ct));
    var categoryIdProp = afterDoc.RootElement.GetProperty("categoryId");
    Assert.Equal(JsonValueKind.Null, categoryIdProp.ValueKind);

    // Listing usług (orphany widoczne) nadal zwraca tę usługę.
    var listResponse = await client.GetAsync("/api/Services", ct);
    using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
    var ids = listDoc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    Assert.Contains(seed.ServiceId, ids);

    // Kategoria zniknęła z listy (soft-deleted, odfiltrowana).
    var categoriesResponse = await client.GetAsync("/api/ServiceCategories", ct);
    using var catDoc = JsonDocument.Parse(await categoriesResponse.Content.ReadAsStringAsync(ct));
    var categoryIds = catDoc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    Assert.DoesNotContain(seed.ServiceCategoryId, categoryIds);
  }
}
