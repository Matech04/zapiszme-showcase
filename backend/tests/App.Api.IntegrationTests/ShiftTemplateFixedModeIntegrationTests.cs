using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Regresja: tworzenie i edycja szablonu zmiany w trybie stałych godzin przez endpoint
/// (POST/PUT /api/ShiftTemplates). Walidator szablonu jest mode-aware — szablon stały ma tylko
/// FixedStartTimes (WorkRanges puste), więc bez tego padałby na 400 (jak wcześniej grafik stały).
/// </summary>
public sealed class ShiftTemplateFixedModeIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  private sealed record TemplateDto(Guid id, string name, int slotGenerationMode, string[] fixedStartTimes);

  [Fact]
  public async Task Create_and_edit_fixed_template_through_endpoint()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // CREATE (fixed mode)
    var createResp = await ownerClient.PostAsJsonAsync(
      "/api/ShiftTemplates",
      new
      {
        name = "Stałe poranne",
        slotGenerationMode = 1,
        workRanges = Array.Empty<object>(),
        breaks = Array.Empty<object>(),
        fixedStartTimes = new[] { "09:00:00", "12:00:00" },
      },
      ct);
    var createBody = await createResp.Content.ReadAsStringAsync(ct);
    Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {(int)createResp.StatusCode}: {createBody}");
    var id = await createResp.Content.ReadFromJsonAsync<Guid>(JsonOpts, ct);

    // GET → verify round-trip
    var getResp = await ownerClient.GetAsync($"/api/ShiftTemplates/{id}", ct);
    Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    var dto = await getResp.Content.ReadFromJsonAsync<TemplateDto>(JsonOpts, ct);
    Assert.Equal(1, dto!.slotGenerationMode);
    Assert.Equal(new[] { "09:00:00", "12:00:00" }, dto.fixedStartTimes);

    // EDIT (fixed → fixed, change times)
    var putResp = await ownerClient.PutAsJsonAsync(
      $"/api/ShiftTemplates/{id}",
      new
      {
        name = "Stałe poranne v2",
        slotGenerationMode = 1,
        workRanges = Array.Empty<object>(),
        breaks = Array.Empty<object>(),
        fixedStartTimes = new[] { "10:00:00", "13:00:00", "16:00:00" },
      },
      ct);
    var putBody = await putResp.Content.ReadAsStringAsync(ct);
    Assert.True(putResp.IsSuccessStatusCode, $"Update failed: {(int)putResp.StatusCode}: {putBody}");

    var getResp2 = await ownerClient.GetAsync($"/api/ShiftTemplates/{id}", ct);
    var dto2 = await getResp2.Content.ReadFromJsonAsync<TemplateDto>(JsonOpts, ct);
    Assert.Equal("Stałe poranne v2", dto2!.name);
    Assert.Equal(new[] { "10:00:00", "13:00:00", "16:00:00" }, dto2.fixedStartTimes);
  }
}
