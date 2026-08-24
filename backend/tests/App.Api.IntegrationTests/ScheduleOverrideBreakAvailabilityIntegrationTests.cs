using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Regresja dla panelowej funkcji „szybkich przerw": przerwa zapisana jako dzień specjalny
/// (override Grid z `breaks`) zdejmuje odpowiednie sloty z available-slots, a usunięcie override
/// przywraca je. Round-trip przez realne endpointy, których używa panel
/// (POST/DELETE /api/Employees/{id}/schedule-overrides + GET /api/Appointments/available-slots).
/// </summary>
public sealed class ScheduleOverrideBreakAvailabilityIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  // Wtorek w przyszłości — objęty grafikiem seeda (Pn–Nd 08:00–20:00), więc bez filtra „przeszłości".
  // Data LICZONA, nie zaszyta — stała „2026-08-04" po tym dniu czyniła termin przeszłym,
  // więc sloty znikały niezależnie od testowanego wyjątku w grafiku.
  private static readonly string Date =
    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");

  private sealed record SlotDto(string slot, bool isPreferred);

  private static async Task<List<string>> SlotsAsync(
    HttpClient client, Guid employeeId, Guid serviceId, CancellationToken ct)
  {
    var resp = await client.GetAsync(
      $"/api/Appointments/available-slots?date={Date}&employeeId={employeeId}&serviceIds={serviceId}", ct);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var slots = await resp.Content.ReadFromJsonAsync<List<SlotDto>>(JsonOpts, ct);
    Assert.NotNull(slots);
    return slots!.Select(s => s.slot).ToList();
  }

  [Fact]
  public async Task Break_override_removes_slots_and_removing_override_restores_them()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // 1. Baseline — grafik tygodniowy 08:00–20:00, bez przerw → slot 12:00 dostępny.
    var baseline = await SlotsAsync(client, seed.EmployeeId, seed.ServiceId, ct);
    Assert.Contains("12:00", baseline);

    // 2. Override Grid: te same godziny + przerwa 12:00–13:00 (jak dodaje to panel).
    var postResp = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/schedule-overrides",
      new
      {
        date = Date,
        slotGenerationMode = 0, // Grid
        workRanges = new[] { new { startTime = "08:00:00", endTime = "20:00:00" } },
        breaks = new[] { new { startTime = "12:00:00", endTime = "13:00:00" } },
        fixedStartTimes = Array.Empty<object>(),
      },
      ct);
    Assert.True(
      postResp.IsSuccessStatusCode,
      $"POST override: {(int)postResp.StatusCode} {await postResp.Content.ReadAsStringAsync(ct)}");

    // 3. Po dodaniu — żaden slot startu nie wpada w okno przerwy [12:00, 13:00).
    var afterAdd = await SlotsAsync(client, seed.EmployeeId, seed.ServiceId, ct);
    Assert.DoesNotContain(
      afterAdd,
      s => string.CompareOrdinal(s, "12:00") >= 0 && string.CompareOrdinal(s, "13:00") < 0);
    // Slot przed przerwą zostaje (usługa 30 min kończy się o 11:30, nie wchodzi w przerwę).
    Assert.Contains("11:00", afterAdd);

    // 4. Usunięcie override → 204.
    var del = await client.DeleteAsync(
      $"/api/Employees/{seed.EmployeeId}/schedule-overrides/{Date}", ct);
    Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

    // 5. Sloty wracają identycznie jak baseline.
    var restored = await SlotsAsync(client, seed.EmployeeId, seed.ServiceId, ct);
    Assert.Equal(baseline, restored);
  }
}
