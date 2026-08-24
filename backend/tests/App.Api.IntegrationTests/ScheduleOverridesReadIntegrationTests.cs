using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Api.IntegrationTests;

/// <summary>
/// Treść odpowiedzi endpointu nadpisań grafiku (dni specjalnych). Istniejące testy sprawdzały
/// wyłącznie autoryzację, więc payload nie był chroniony.
///
/// Powód powstania: handler przeszedł z materializacji encji `Employee` (ciągnącej cały agregat —
/// zmierzony SQL łączył dziewięć tabel) na projekcję. Projekcja MUSI iść przez `o.ScheduleDay.*`,
/// bo `o.WorkRanges` / `o.Breaks` / `o.FixedStartTimes` / `o.IsFixed` to właściwości wyliczane bez
/// mapowania — nie tłumaczą się na SQL. Te testy pilnują, że oba tryby (siatka i stałe godziny)
/// wracają kompletne, bo od nich zależy wyliczanie dostępności terminów.
/// </summary>
public sealed class ScheduleOverridesReadIntegrationTests
{
  [Fact]
  public async Task Tryb_siatki_zwraca_zakresy_pracy_i_przerwy()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var zapis = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/schedule-overrides",
      new
      {
        date = TestDates.IsoInDays(14),
        slotGenerationMode = SlotGenerationMode.Grid,
        workRanges = new[] { new { startTime = "09:00:00", endTime = "17:00:00" } },
        breaks = new[] { new { startTime = "12:00:00", endTime = "12:30:00" } },
        fixedStartTimes = Array.Empty<string>(),
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, zapis.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/schedule-overrides", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var lista = await response.Content.ReadFromJsonAsync<List<ScheduleOverrideDto>>(ct);
    Assert.NotNull(lista);
    var dzien = Assert.Single(lista!);

    Assert.Equal(TestDates.InDays(14), dzien.Date);
    Assert.Equal(SlotGenerationMode.Grid, dzien.SlotGenerationMode);
    var zakres = Assert.Single(dzien.WorkRanges!);
    Assert.Equal(new TimeOnly(9, 0), zakres.StartTime);
    Assert.Equal(new TimeOnly(17, 0), zakres.EndTime);
    var przerwa = Assert.Single(dzien.Breaks!);
    Assert.Equal(new TimeOnly(12, 0), przerwa.StartTime);
    Assert.Equal(new TimeOnly(12, 30), przerwa.EndTime);
  }

  [Fact]
  public async Task Tryb_stalych_godzin_zwraca_FixedStartTimes_i_wlasciwy_tryb()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var zapis = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/schedule-overrides",
      new
      {
        date = TestDates.IsoInDays(15),
        slotGenerationMode = SlotGenerationMode.FixedStartTimes,
        workRanges = Array.Empty<object>(),
        breaks = Array.Empty<object>(),
        fixedStartTimes = new[] { "10:00:00", "14:00:00" },
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, zapis.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/schedule-overrides", ct);
    var lista = await response.Content.ReadFromJsonAsync<List<ScheduleOverrideDto>>(ct);
    Assert.NotNull(lista);
    var dzien = Assert.Single(lista!);

    // Kluczowe dla projekcji: tryb wyznaczamy teraz jako `FixedStartTimes.Count > 0` zamiast
    // wyliczanej właściwości `IsFixed`. Gdyby to się nie przetłumaczyło albo dało zły wynik,
    // kalendarz generowałby sloty siatką zamiast stałych godzin.
    Assert.Equal(SlotGenerationMode.FixedStartTimes, dzien.SlotGenerationMode);
    Assert.Equal(
      new[] { new TimeOnly(10, 0), new TimeOnly(14, 0) },
      dzien.FixedStartTimes!.ToArray());
  }

  [Fact]
  public async Task Sortowanie_po_dacie_i_404_dla_nieistniejacego_pracownika()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    foreach (var data in new[] { TestDates.IsoInDays(45), TestDates.IsoInDays(20) })
    {
      var zapis = await client.PostAsJsonAsync(
        $"/api/Employees/{seed.EmployeeId}/schedule-overrides",
        new
        {
          date = data,
          slotGenerationMode = SlotGenerationMode.Grid,
          workRanges = new[] { new { startTime = "09:00:00", endTime = "17:00:00" } },
          breaks = Array.Empty<object>(),
          fixedStartTimes = Array.Empty<string>(),
        },
        ct);
      Assert.Equal(HttpStatusCode.OK, zapis.StatusCode);
    }

    var lista = await (await client.GetAsync($"/api/Employees/{seed.EmployeeId}/schedule-overrides", ct))
      .Content.ReadFromJsonAsync<List<ScheduleOverrideDto>>(ct);
    Assert.NotNull(lista);
    Assert.Equal(
      new[] { TestDates.InDays(20), TestDates.InDays(45) },
      lista!.Select(x => x.Date).ToArray());

    var brak = await client.GetAsync($"/api/Employees/{Guid.NewGuid()}/schedule-overrides", ct);
    Assert.Equal(HttpStatusCode.NotFound, brak.StatusCode);
  }
}
