using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Regresja: wyjątek (override) w trybie stałych godzin przez endpoint
/// (POST/GET /api/Employees/{id}/schedule-overrides) — round-trip na realnym Postgresie,
/// łącznie z kolumną fixed_start_times i polem slotGenerationMode.
/// </summary>
public sealed class ScheduleOverrideFixedModeIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  private sealed record OverrideDto(string date, int slotGenerationMode, string[] fixedStartTimes, object[] workRanges);

  [Fact]
  public async Task Create_and_read_fixed_override_through_endpoint()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var postResp = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/schedule-overrides",
      new
      {
        date = TestDates.IsoInDays(14),
        slotGenerationMode = 1,
        workRanges = Array.Empty<object>(),
        breaks = Array.Empty<object>(),
        fixedStartTimes = new[] { "09:00:00", "15:00:00" },
      },
      ct);
    var postBody = await postResp.Content.ReadAsStringAsync(ct);
    Assert.True(postResp.IsSuccessStatusCode, $"POST override failed: {(int)postResp.StatusCode}: {postBody}");

    var getResp = await ownerClient.GetAsync($"/api/Employees/{seed.EmployeeId}/schedule-overrides", ct);
    Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    var overrides = await getResp.Content.ReadFromJsonAsync<List<OverrideDto>>(JsonOpts, ct);

    var ovr = Assert.Single(overrides!);
    Assert.Equal(1, ovr.slotGenerationMode);
    Assert.Equal(new[] { "09:00:00", "15:00:00" }, ovr.fixedStartTimes);
    Assert.Empty(ovr.workRanges);
  }
}
