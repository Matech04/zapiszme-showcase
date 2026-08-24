using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// ONBOARDING-SCHEDULE — krok kreatora „Kiedy pracujesz?" (<c>POST /api/onboarding/schedule</c>).
///
/// Regresja: <c>SetOnboardingScheduleCommand</c> budował <c>EmployeeScheduleDto</c> z
/// <c>ActiveFrom = DateOnly.MinValue</c>, a walidator <c>EmployeeScheduleDto</c> odrzuca
/// domyślną datę (<c>Must(NotDefaultDate)</c>) → każde wywołanie kroku grafiku zwracało 400 i
/// właściciel nie mógł ukończyć onboardingu. Poprzednie testy onboardingu kończyły się na
/// kroku „profil" i nie przechodziły przez ten walidator, więc luka przeszła niezauważona.
/// Te testy przechodzą pełny walidator dla OBU trybów (Grid + FixedStartTimes).
/// </summary>
public sealed class OnboardingScheduleIntegrationTests
{
  [Fact]
  public async Task Grid_schedule_step_succeeds_and_persists_employee_schedule()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-grid.local", "+48501222001", "Grid Sched Salon", "grid-sched-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[]
        {
          new { day = 1, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 2, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 3, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 4, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 5, startTime = "09:00:00", endTime = "17:00:00" },
        },
        fixedStartTimes = (string[]?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);
    Assert.NotEmpty(employee.Schedules);

    // Tryb siatki włącza PreferAdjacent na salonie (bez pytania właściciela — wg planu).
    var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
    Assert.Equal(GapFillingMode.PreferAdjacent, tenant.GapFillingSettings?.Mode);
  }

  /// <summary>
  /// Różne godziny w różne dni (Pn 9–17, Wt 10–18). Wcześniej komenda miała JEDEN wspólny
  /// StartTime/EndTime rozwijany na wszystkie dni, więc właścicielka o zmiennych godzinach kończyła
  /// kreator z BŁĘDNYM grafikiem — klientka rezerwowała wtorek na 9:00, choć praca zaczyna się o 10.
  /// </summary>
  [Fact]
  public async Task Grid_schedule_step_persists_different_hours_per_day()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-perday.local", "+48501222005", "PerDay Salon", "perday-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[]
        {
          new { day = 1, startTime = "09:00:00", endTime = "17:00:00" }, // Pn
          new { day = 2, startTime = "10:00:00", endTime = "18:00:00" }, // Wt — INNE godziny
        },
        fixedStartTimes = (string[]?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);

    // Dzień identyfikuje CycleIndex: przy jednym cyklu to wprost (int)DayOfWeek
    // (C#: niedziela=0, poniedziałek=1) — patrz Employee.ResolveScheduleDay.
    var days = employee.Schedules.Single().ScheduleDays;
    var monday = days.Single(d => d.CycleIndex == (int)DayOfWeek.Monday);
    var tuesday = days.Single(d => d.CycleIndex == (int)DayOfWeek.Tuesday);

    Assert.Equal(new TimeOnly(9, 0), monday.WorkRanges.Single().StartTime);
    Assert.Equal(new TimeOnly(17, 0), monday.WorkRanges.Single().EndTime);
    // Sedno: wtorek trzyma SWOJE godziny, nie poniedziałkowe.
    Assert.Equal(new TimeOnly(10, 0), tuesday.WorkRanges.Single().StartTime);
    Assert.Equal(new TimeOnly(18, 0), tuesday.WorkRanges.Single().EndTime);
  }

  /// <summary>
  /// „Papierowy kalendarz": właściciel deklaruje, że nie prowadzi grafiku powtarzalnego.
  /// Krok grafiku ma się wtedy domknąć BEZ tworzenia jakiegokolwiek grafiku — inaczej kreator
  /// wypuszczałby ją z fikcyjnym Pn–Pt 9–17, który od razu generuje terminy dla klientek.
  ///
  /// Silnik dostępności obsługuje ten tryb od dawna (patrz OverrideOnlyFixedDayIntegrationTests) —
  /// brakowało wyłącznie sposobu, żeby go ZADEKLAROWAĆ.
  /// </summary>
  [Fact]
  public async Task Ad_hoc_schedule_step_creates_no_schedule_and_unblocks_the_wizard()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-adhoc.local", "+48501222003", "AdHoc Salon", "adhoc-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);

    // Branża jest krokiem PRZED grafikiem — bez niej stan słusznie wskazywałby „Industry"
    // i test nie dowodziłby niczego o kroku grafiku.
    var industryResponse = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/industry",
      new { industryKey = "nails", services = Array.Empty<object>() },
      ct);
    Assert.Equal(HttpStatusCode.OK, industryResponse.StatusCode);

    var response = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        // Tryb z poprzedniego kroku („Jak wyznaczasz terminy?") leci TAKŻE przy ad-hoc.
        slotMode = (int)SlotGenerationMode.FixedStartTimes,
        days = Array.Empty<object>(),
        fixedStartTimes = (string[]?)null,
        useAdHoc = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);

    Assert.True(employee.UsesAdHocSchedule);
    Assert.Empty(employee.Schedules);

    // Tryb MUSI się zapisać mimo braku grafiku: to podpowiedź startowa dla każdego dnia specjalnego,
    // który właścicielka doda później. Wcześniej gałąź ad-hoc robiła wczesny return i wybór
    // z kroku „Jak wyznaczasz terminy?" przepadał po cichu — każdy dzień startowałby jako siatka.
    Assert.Equal(SlotGenerationMode.FixedStartTimes, employee.SlotGenerationMode);

    // Kluczowe: kreator przestaje domagać się grafiku i przepuszcza dalej. „Schedule" znaczy tu
    // wąsko „grafik domknięty, został sam przycisk kończący" — od przeniesienia kroku „Zapisy"
    // przed grafik to grafik jest ostatnim krokiem treściowym i to on woła `complete()`.
    // Ten stan zobaczy właścicielka tylko wtedy, gdy domknięcie padnie (front robi oba wywołania
    // pod jednym przyciskiem); wraca wtedy na grafik, a nie na początek trójki.
    var stateResponse = await ownerClient.GetAsync("/api/onboarding/state", ct);
    var state = await stateResponse.Content.ReadFromJsonAsync<AdHocStateProbe>(
      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    Assert.NotNull(state);
    Assert.True(state!.UsesAdHocSchedule);
    Assert.False(state.HasSchedule);
    Assert.Equal("Schedule", state.NextStep);
  }

  /// <summary>
  /// Powrót na krok grafiku i poprawka godzin („Dalej → Wstecz → popraw → Dalej") musi zaktualizować
  /// istniejący grafik, nie dołożyć drugi.
  ///
  /// Wcześniej komenda przekazywała `Id: null`, więc `SetEmployeeSchedule` wołało AddSchedule —
  /// a to rzuca SchedulesCollision, gdy aktywny zakres nachodzi na istniejący. Kreator zawsze zakłada
  /// „od dziś bezterminowo", więc drugie przejście kroku kolidowało SAMO ZE SOBĄ i użytkownik
  /// dostawał „serwer odrzucił żądanie" bez żadnej możliwości poprawienia godzin.
  /// </summary>
  [Fact]
  public async Task Repeating_the_schedule_step_updates_the_schedule_instead_of_colliding()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-again.local", "+48501222007", "Again Salon", "again-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);

    var first = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[] { new { day = 1, startTime = "09:00:00", endTime = "17:00:00" } },
        fixedStartTimes = (string[]?)null,
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

    // Powrót i poprawka godzin — to samo „od dziś bezterminowo", inne godziny.
    var second = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[] { new { day = 1, startTime = "11:00:00", endTime = "19:00:00" } },
        fixedStartTimes = (string[]?)null,
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);

    // JEDEN grafik, z NOWYMI godzinami — nie dwa, nie stare.
    var schedule = Assert.Single(employee.Schedules);
    var monday = schedule.ScheduleDays.Single(d => d.CycleIndex == (int)DayOfWeek.Monday);
    Assert.Equal(new TimeOnly(11, 0), monday.WorkRanges.Single().StartTime);
    Assert.Equal(new TimeOnly(19, 0), monday.WorkRanges.Single().EndTime);
  }

  /// <summary>
  /// Przełączenie NA „na bieżąco" po utworzeniu grafiku musi go WYCISZYĆ. Ścieżka jest realna:
  /// „powtarzalny" → Dalej (grafik powstaje) → Wstecz → „na bieżąco" → Dalej.
  ///
  /// Bez tego stary grafik zostaje AKTYWNY, a <c>ResolveScheduleDay</c> (filtruje po IsActive) dalej
  /// generuje z niego terminy — salon pokazuje klientkom Pn–Pt 9–17 mimo deklaracji właścicielki,
  /// że tak nie pracuje. Błąd niewidoczny dla niej samej: kreator mówi „na bieżąco", a rezerwacje
  /// lecą ze starego grafiku.
  /// </summary>
  [Fact]
  public async Task Switching_to_ad_hoc_deactivates_the_existing_schedule()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-silence.local", "+48501222006", "Silence Salon", "silence-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);

    // Najpierw grafik powtarzalny…
    await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[] { new { day = 1, startTime = "09:00:00", endTime = "17:00:00" } },
        fixedStartTimes = (string[]?)null,
      },
      ct);

    // …a potem zmiana zdania na „na bieżąco".
    var response = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = Array.Empty<object>(),
        fixedStartTimes = (string[]?)null,
        useAdHoc = true,
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);

    Assert.True(employee.UsesAdHocSchedule);
    // Grafik zostaje (powrót nie ma kosztować utraty ustawień), ale MUSI być wyciszony.
    Assert.NotEmpty(employee.Schedules);
    Assert.All(employee.Schedules, s => Assert.False(s.IsActive));
  }

  /// <summary>
  /// Powrót z papierowego kalendarza do grafiku powtarzalnego („Wstecz" w kreatorze) musi zdjąć
  /// deklarację — inaczej właścicielka miałaby grafik I flagę „nie prowadzę grafiku".
  /// </summary>
  [Fact]
  public async Task Setting_a_recurring_schedule_clears_the_ad_hoc_declaration()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-switch.local", "+48501222004", "Switch Salon", "switch-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);

    await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new { slotMode = (int)SlotGenerationMode.Grid, days = Array.Empty<object>(), fixedStartTimes = (string[]?)null, useAdHoc = true },
      ct);

    var switchBack = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.Grid,
        days = new[]
        {
          new { day = 1, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 2, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 3, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 4, startTime = "09:00:00", endTime = "17:00:00" },
          new { day = 5, startTime = "09:00:00", endTime = "17:00:00" },
        },
        fixedStartTimes = (string[]?)null,
        useAdHoc = false,
      },
      ct);
    Assert.Equal(HttpStatusCode.NoContent, switchBack.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);

    Assert.False(employee.UsesAdHocSchedule);
    Assert.NotEmpty(employee.Schedules);
  }

  private sealed record AdHocStateProbe(bool HasSchedule, bool UsesAdHocSchedule, string NextStep);

  [Fact]
  public async Task Fixed_start_times_schedule_step_succeeds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@sched-fixed.local", "+48501222002", "Fixed Sched Salon", "fixed-sched-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new
      {
        slotMode = (int)SlotGenerationMode.FixedStartTimes,
        // Tryb stały: dni BEZ godzin — dzień nie ma okna pracy, liczą się tylko godziny startu.
        days = new[]
        {
          new { day = 1, startTime = (string?)null, endTime = (string?)null },
          new { day = 3, startTime = (string?)null, endTime = (string?)null },
          new { day = 5, startTime = (string?)null, endTime = (string?)null },
        },
        fixedStartTimes = new[] { "09:00:00", "12:00:00", "15:00:00" },
      },
      ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.Schedules)
      .AsNoTracking()
      .SingleAsync(e => e.Id == employeeId, ct);
    Assert.NotEmpty(employee.Schedules);
  }
}
