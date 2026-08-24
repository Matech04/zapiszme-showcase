using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Treść odpowiedzi endpointu urlopów. Istniejące testy kalendarza sprawdzają wyłącznie
/// autoryzację (200/403), więc sama zawartość payloadu nie była niczym chroniona.
///
/// Powód powstania: handler przeszedł z materializacji encji `Employee` na projekcję. Encja
/// ciągnęła ZA SOBĄ wszystkie cztery kolekcje owned (zmierzony SQL: dziewięć tabel w jednym
/// SELECT, iloczyn kartezjański), a projekcja schodzi do dwóch tabel. Te testy pilnują, że przy
/// okazji nie zgubiliśmy pól, kolejności ani rozróżnienia „nie ma pracownika" od „nie ma urlopów".
/// </summary>
public sealed class EmployeeLeavesReadIntegrationTests
{
  [Fact]
  public async Task Zwraca_urlopy_z_kompletem_pol_posortowane_po_dacie_startu()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Daty liczone; istotne są RELACJE, nie konkretne dni: dwa rozłączne zakresy, ten dodany
    // jako pierwszy jest późniejszy w kalendarzu. Zakresy nie mogą się nakładać — domena rzuca
    // wtedy LeaveOverlapException.
    var wczesniejszyStart = TestDates.InDays(10);
    var wczesniejszyKoniec = TestDates.InDays(14);
    var pozniejszyStart = TestDates.InDays(40);
    var pozniejszyKoniec = TestDates.InDays(42);

    // Dodajemy w kolejności ODWROTNEJ do oczekiwanej, żeby test faktycznie sprawdzał sortowanie,
    // a nie przypadkową kolejność wstawiania.
    var pozniejszy = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/leaves",
      new
      {
        startDate = pozniejszyStart.ToString("yyyy-MM-dd"),
        endDate = pozniejszyKoniec.ToString("yyyy-MM-dd"),
        absenceType = AbsenceType.Vacation,
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, pozniejszy.StatusCode);

    var wczesniejszy = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/leaves",
      new
      {
        startDate = wczesniejszyStart.ToString("yyyy-MM-dd"),
        endDate = wczesniejszyKoniec.ToString("yyyy-MM-dd"),
        absenceType = AbsenceType.SickLeave,
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, wczesniejszy.StatusCode);

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/leaves", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var leaves = await response.Content.ReadFromJsonAsync<List<EmployeeLeaveDto>>(ct);
    Assert.NotNull(leaves);
    Assert.Equal(2, leaves!.Count);

    Assert.Equal(wczesniejszyStart, leaves[0].StartDate);
    Assert.Equal(wczesniejszyKoniec, leaves[0].EndDate);
    Assert.Equal(AbsenceType.SickLeave, leaves[0].AbsenceType);
    Assert.NotEqual(Guid.Empty, leaves[0].Id);

    Assert.Equal(pozniejszyStart, leaves[1].StartDate);
    Assert.Equal(pozniejszyKoniec, leaves[1].EndDate);
    Assert.Equal(AbsenceType.Vacation, leaves[1].AbsenceType);
  }

  [Fact]
  public async Task Pracownik_bez_urlopow_zwraca_pusta_liste_a_nie_404()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Employees/{seed.EmployeeId}/leaves", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var leaves = await response.Content.ReadFromJsonAsync<List<EmployeeLeaveDto>>(ct);
    Assert.NotNull(leaves);
    Assert.Empty(leaves!);
  }

  /// <summary>
  /// `GetByIdWithLeavesAsync` dostało `AsSplitQuery()` — agregat wczytuje się teraz kilkoma
  /// zapytaniami zamiast jednym z iloczynem kartezjańskim (zmierzone na produkcji: 8 316 wierszy
  /// po 551 B na jednego pracownika). Ryzykiem takiej zmiany jest kolekcja, która wróci PUSTA:
  /// change tracker uzna wtedy jej wiersze za usunięte i `SaveChanges` wyczyści je z bazy.
  /// Dokładnie ta pułapka jest już opisana przy `GetByIdAsync` i `MonthPublications`.
  ///
  /// OGRANICZENIE: w domyślnym przebiegu (InMemory) `AsSplitQuery` jest ignorowane, więc ten test
  /// sprawdza wtedy tylko, że dodanie urlopu nie gubi kolekcji — NIE samo rozbicie zapytania.
  /// Realną wartość ma dopiero na Postgresie; patrz uwaga o `INTEGRATION_DB_PROVIDER` niżej.
  /// </summary>
  [Fact]
  public async Task Dodanie_urlopu_nie_gubi_pozostalych_kolekcji_agregatu()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    SeedPelnyAgregat(factory.Services, seed.EmployeeId, seed.TenantId, seed.ServiceId);
    var przed = OdczytajLicznosci(factory.Services, seed.EmployeeId);

    var response = await client.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/leaves",
      new
      {
        startDate = TestDates.InDays(10).ToString("yyyy-MM-dd"),
        endDate = TestDates.InDays(14).ToString("yyyy-MM-dd"),
        absenceType = AbsenceType.Vacation,
      },
      ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var po = OdczytajLicznosci(factory.Services, seed.EmployeeId);

    // Urlop przybył...
    Assert.Equal(przed.Urlopy + 1, po.Urlopy);

    // ...a nic poza nim nie zniknęło. To jest właściwa treść tego testu.
    Assert.Equal(przed.Uslugi, po.Uslugi);
    Assert.Equal(przed.Grafiki, po.Grafiki);
    Assert.Equal(przed.DniGrafiku, po.DniGrafiku);
    Assert.Equal(przed.Nadpisania, po.Nadpisania);
    Assert.Equal(przed.Publikacje, po.Publikacje);

    // Kontrola samego seeda — gdyby kolekcje były puste PRZED zapisem, test przechodziłby na pusto.
    Assert.True(przed.Uslugi > 0);
    Assert.True(przed.Grafiki > 0);
    Assert.True(przed.DniGrafiku > 0);
    Assert.True(przed.Nadpisania > 0);
    Assert.True(przed.Publikacje > 0);
  }

  private sealed record Licznosci(
    int Uslugi, int Grafiki, int DniGrafiku, int Nadpisania, int Urlopy, int Publikacje);

  private static void SeedPelnyAgregat(
    IServiceProvider rootServices, Guid employeeId, Guid tenantId, Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = WczytajAgregat(db, employeeId);

    if (employee.Services.All(s => s.ServiceId != serviceId))
    {
      employee.AssignService(tenantId, serviceId, customDuration: null, customPrice: null);
    }

    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
      [DayOfWeek.Monday] = [new TimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0))],
      [DayOfWeek.Tuesday] = [new TimeRange(new TimeOnly(10, 0), new TimeOnly(18, 0))],
    });

    employee.SetScheduleOverride(
      TestDates.InDays(30),
      [new TimeRange(new TimeOnly(12, 0), new TimeOnly(16, 0))]);

    var publikacja = TestDates.InDays(90);
    employee.SetMonthPublication(publikacja.Year, publikacja.Month, opensOn: null);

    employee.AddLeave(TestDates.InDays(80), TestDates.InDays(84), AbsenceType.Vacation);

    db.SaveChanges();
  }

  private static Licznosci OdczytajLicznosci(IServiceProvider rootServices, Guid employeeId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = WczytajAgregat(db, employeeId);

    return new Licznosci(
      employee.Services.Count,
      employee.Schedules.Count,
      employee.Schedules.Sum(s => s.ScheduleDays.Count),
      employee.Overrides.Count,
      employee.Leaves.Count,
      employee.MonthPublications.Count);
  }

  private static Employee WczytajAgregat(ApplicationDbContext db, Guid employeeId) =>
    db.Employees
      .IgnoreQueryFilters()
      .AsSplitQuery()
      .Include(e => e.Services)
      .Include(e => e.Schedules)
      .ThenInclude(s => s.ScheduleDays)
      .Include(e => e.Overrides)
      .Include(e => e.Leaves)
      .Include(e => e.MonthPublications)
      .Single(e => e.Id == employeeId);

  [Fact]
  public async Task Nieistniejacy_pracownik_zwraca_404_a_nie_pusta_liste()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Rozróżnienie istotne po przejściu na projekcję: `FirstOrDefaultAsync` na projekcji listy
    // zwraca null dla braku pracownika i pustą listę dla pracownika bez urlopów. Gdyby handler
    // mylił te przypadki, panel pokazałby „brak urlopów" dla usuniętego pracownika.
    var response = await client.GetAsync($"/api/Employees/{Guid.NewGuid()}/leaves", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
