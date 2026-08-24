using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Api.IntegrationTests;

/// <summary>
/// Treść odpowiedzi endpointu grafików pracownika — najgłębsza z trzech projekcji
/// (Schedules → ScheduleDays → {WorkRanges, Breaks}).
///
/// Powód powstania: handler przeszedł z materializacji encji `Employee` na projekcję. Encja
/// ciągnęła cały agregat (zmierzony SQL: dziewięć tabel w jednym SELECT, iloczyn kartezjański).
/// Zapytanie zasila WYŁĄCZNIE ten endpoint odczytu — wyliczanie dostępności idzie przez
/// `AppointmentService.IsAvailableAsync`, które czyta agregat wprost i tej projekcji nie dotyka.
/// Mimo to od tych danych zależy, co personel widzi i edytuje w kreatorze grafiku.
/// </summary>
public sealed class EmployeeSchedulesReadIntegrationTests
{
  [Fact]
  public async Task Zwraca_grafik_z_dniami_i_zakresami_pracy()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Seed zakłada grafik tygodniowy 08:00-20:00 na każdy dzień — czytamy właśnie jego,
    // bo dołożenie drugiego grafiku na ten sam okres kończy się kolizją w domenie.
    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/employee-schedules", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var lista = await response.Content.ReadFromJsonAsync<List<EmployeeScheduleDto>>(ct);
    Assert.NotNull(lista);
    var grafik = Assert.Single(lista!);

    Assert.NotNull(grafik.Id);
    Assert.True(grafik.IsActive);
    Assert.Equal(SlotGenerationMode.Grid, grafik.SlotGenerationMode);
    Assert.NotEmpty(grafik.Days);

    // Każdy dzień cyklu musi wrócić z zakresem pracy — to sprawdza, że zagnieżdżona projekcja
    // ScheduleDays -> WorkRanges faktycznie się przetlumaczyla i nic nie zgubila.
    foreach (var dzien in grafik.Days)
    {
      var zakres = Assert.Single(dzien.WorkRanges!);
      Assert.Equal(new TimeOnly(8, 0), zakres.StartTime);
      Assert.Equal(new TimeOnly(20, 0), zakres.EndTime);
    }
  }

  [Fact]
  public async Task Tryb_wynika_z_dni_TEGO_grafiku_a_nie_z_globalnego_ustawienia()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Najpierw kasujemy grafik z seeda — inaczej domena odrzuca nowy jako kolidujacy.
    var istniejace = await (await client.GetAsync($"/api/Employees/{seed.EmployeeId}/employee-schedules", ct))
      .Content.ReadFromJsonAsync<List<EmployeeScheduleDto>>(ct);
    foreach (var g in istniejace!)
    {
      var usun = await client.DeleteAsync($"/api/Employees/{seed.EmployeeId}/employee-schedules/{g.Id}", ct);
      Assert.Equal(HttpStatusCode.NoContent, usun.StatusCode);
    }

    var zapis = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/employee-schedules",
      new
      {
        activeFrom = TestDates.IsoMonthStart(1),
        activeTo = TestDates.MonthStart(2).AddDays(-1).ToString("yyyy-MM-dd"),
        numberOfCycles = 1,
        days = new[]
        {
          new
          {
            cycleIndex = 0,
            workRanges = Array.Empty<object>(),
            breaks = Array.Empty<object>(),
            fixedStartTimes = new[] { "09:30:00", "13:00:00" },
          },
        },
        slotGenerationMode = SlotGenerationMode.FixedStartTimes,
        isActive = true,
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, zapis.StatusCode);

    var lista = await (await client.GetAsync($"/api/Employees/{seed.EmployeeId}/employee-schedules", ct))
      .Content.ReadFromJsonAsync<List<EmployeeScheduleDto>>(ct);
    var grafik = Assert.Single(lista!.Where(g => g.ActiveFrom == TestDates.MonthStart(1)));

    // Tryb liczony w SQL jako `ScheduleDays.Any(d => d.FixedStartTimes.Count > 0)`. Gdyby to
    // przestało działać, grafik ze stałymi godzinami renderowałby się jako pusta siatka.
    Assert.Equal(SlotGenerationMode.FixedStartTimes, grafik.SlotGenerationMode);
    var dzien = Assert.Single(grafik.Days);
    Assert.Equal(
      new[] { new TimeOnly(9, 30), new TimeOnly(13, 0) },
      dzien.FixedStartTimes!.ToArray());
  }

  [Fact]
  public async Task Nieistniejacy_pracownik_zwraca_404()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Employees/{Guid.NewGuid()}/employee-schedules", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
