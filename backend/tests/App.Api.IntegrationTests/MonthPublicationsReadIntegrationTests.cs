using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Employees.Dtos;

namespace App.Api.IntegrationTests;

/// <summary>
/// Treść odpowiedzi endpointu publikacji miesięcy — parytet z testami odczytu grafiku, urlopów
/// i dni specjalnych.
///
/// Handler celowo NIE materializuje encji `Employee` (kolekcje owned jadą z rodzicem zawsze, więc
/// odczyt kilku wierszy ciągnąłby cały agregat), tylko projektuje. Te testy pilnują, że projekcja
/// oddaje komplet — w szczególności `OpensOn = null`, które niesie osobne znaczenie („zamknięty
/// bezterminowo"), łatwe do zgubienia przy przepisywaniu zapytania.
/// </summary>
public sealed class MonthPublicationsReadIntegrationTests
{
  [Fact]
  public async Task Zwraca_date_otwarcia_zapisow()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Miesiąc LICZONY — publikacje dotyczą konkretnego roku i miesiąca, więc bierzemy najbliższy
    // przyszły. Zaszyte 2026/9 przestałoby być „przyszłym miesiącem" po wrześniu 2026.
    var miesiac = TestDates.MonthStart(1);

    var zapis = await client.PutAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications",
      new { year = miesiac.Year, month = miesiac.Month, opensOn = miesiac.ToString("yyyy-MM-dd") },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, zapis.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/month-publications", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var lista = await response.Content.ReadFromJsonAsync<List<MonthPublicationDto>>(ct);
    var wpis = Assert.Single(lista!);
    Assert.Equal(miesiac.Year, wpis.Year);
    Assert.Equal(miesiac.Month, wpis.Month);
    Assert.Equal(miesiac, wpis.OpensOn);
  }

  [Fact]
  public async Task Zamkniecie_bezterminowe_wraca_z_pustym_opensOn()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var zapis = await client.PutAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications",
      new { year = TestDates.MonthStart(2).Year, month = TestDates.MonthStart(2).Month, opensOn = (string?)null },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, zapis.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/month-publications", ct);
    var lista = await response.Content.ReadFromJsonAsync<List<MonthPublicationDto>>(ct);

    var wpis = Assert.Single(lista!);
    Assert.Equal(10, wpis.Month);
    Assert.Null(wpis.OpensOn);
  }

  [Fact]
  public async Task Ponowny_zapis_tego_samego_miesiaca_aktualizuje_zamiast_duplikowac()
  {
    // Unikalny indeks (employee_id, year, month) — bez doładowania kolekcji w repozytorium
    // agregat widziałby pustą listę i próbował wstawić drugi wiersz.
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var miesiac = TestDates.MonthStart(1);
    // Druga data otwarcia jest WCZEŚNIEJSZA od pierwszej — sedno testu to nadpisanie wpisu,
    // więc musi się różnić; konkretny dzień nie ma znaczenia.
    var wczesniejszeOtwarcie = miesiac.AddDays(-15);

    await client.PutAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications",
      new { year = miesiac.Year, month = miesiac.Month, opensOn = miesiac.ToString("yyyy-MM-dd") }, ct);
    var drugi = await client.PutAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications",
      new { year = miesiac.Year, month = miesiac.Month, opensOn = wczesniejszeOtwarcie.ToString("yyyy-MM-dd") }, ct);
    Assert.Equal(HttpStatusCode.NoContent, drugi.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/month-publications", ct);
    var lista = await response.Content.ReadFromJsonAsync<List<MonthPublicationDto>>(ct);

    var wpis = Assert.Single(lista!);
    Assert.Equal(wczesniejszeOtwarcie, wpis.OpensOn);
  }

  [Fact]
  public async Task Usuniecie_wpisu_przywraca_domyslny_horyzont()
  {
    // Kasowanie NIE zamyka miesiąca — zdejmuje jawną decyzję, więc lista wraca pusta.
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var miesiac = TestDates.MonthStart(1);

    await client.PutAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications",
      new { year = miesiac.Year, month = miesiac.Month, opensOn = miesiac.ToString("yyyy-MM-dd") }, ct);

    var usuniecie = await client.DeleteAsync(
      $"/api/Employees/{seed.EmployeeId}/month-publications/{miesiac.Year}/{miesiac.Month}", ct);
    Assert.Equal(HttpStatusCode.NoContent, usuniecie.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/month-publications", ct);
    var lista = await response.Content.ReadFromJsonAsync<List<MonthPublicationDto>>(ct);

    Assert.Empty(lista!);
  }
}
