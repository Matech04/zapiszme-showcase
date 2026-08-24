using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Historia wizyt na profilu klienta (`GET /api/Appointments/customer/{id}`).
///
/// Powód powstania: endpoint nie miał ŻADNEGO testu i przez to przepuścił regresję — dołożone
/// `.OrderByDescending(x => x.Date)` sortowało po polach ZPROJEKTOWANEGO DTO, czego EF nie
/// tłumaczy na SQL. Zapytanie wywalało się wyjątkiem, endpoint zwracał 500, a pełny przebieg
/// testów pozostawał zielony. Sortowanie zapewnia `orderby` w składni zapytaniowej PRZED
/// projekcją; ten test pilnuje jednego i drugiego.
///
/// Uwaga: łapie to WYŁĄCZNIE przebieg na Postgresie (`INTEGRATION_DB_PROVIDER=Postgres`).
/// Na InMemory LINQ wykonuje się po stronie klienta i nieprzetłumaczalne wyrażenie przechodzi.
/// </summary>
public sealed class CustomerAppointmentHistoryIntegrationTests
{
  [Fact]
  public async Task Zwraca_historie_wizyt_klienta_posortowana_od_najnowszej()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Dwie wizyty w odwrotnej kolejności do oczekiwanej — sprawdzamy realne sortowanie.
    // Daty liczone: obie muszą być w PRZYSZŁOŚCI (wizyty zakładamy przez API, które odrzuca
    // terminy przeszłe), a odstęp między nimi zachowuje sens testu — starsza i nowsza.
    foreach (var (data, godzina) in new[]
             {
               (TestDates.IsoInDays(14), "10:00:00"),
               (TestDates.IsoInDays(50), "12:00:00"),
             })
    {
      var utworz = await client.PostAsJsonAsync(
        "/api/Appointments",
        new
        {
          employeeId = seed.EmployeeId,
          serviceIds = new[] { seed.ServiceId },
          date = data,
          startTime = godzina,
          customerId = seed.CustomerId,
          customerPhone = (string?)null,
        },
        ct);
      Assert.True(
        utworz.IsSuccessStatusCode,
        $"Nie udało się utworzyć wizyty na {data}: {utworz.StatusCode} {await utworz.Content.ReadAsStringAsync(ct)}");
    }

    var response = await client.GetAsync($"/api/Appointments/customer/{seed.CustomerId}", ct);

    // Sedno regresji: nieprzetłumaczalne sortowanie dawało tu 500, nie pustą listę.
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Czytamy surowy JSON: `AppointmentPreviewDto` niesie domenowy `AppointmentStatus`,
    // którego System.Text.Json nie potrafi zdeserializować po stronie testu.
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var daty = json.RootElement.EnumerateArray()
      .Select(x => DateOnly.Parse(x.GetProperty("date").GetString()!))
      .ToList();

    Assert.True(daty.Count >= 2, $"Oczekiwano co najmniej 2 wizyt, było {daty.Count}.");
    Assert.Equal(daty.OrderByDescending(d => d).ToList(), daty);
  }

  [Fact]
  public async Task Klient_bez_wizyt_zwraca_pusta_liste()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Appointments/customer/{Guid.NewGuid()}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    Assert.Empty(json.RootElement.EnumerateArray());
  }
}
