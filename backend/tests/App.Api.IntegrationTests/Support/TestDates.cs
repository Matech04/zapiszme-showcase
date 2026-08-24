namespace App.Api.IntegrationTests;

/// <summary>
/// Daty w testach są LICZONE od dnia uruchomienia, nigdy zaszyte literałem.
///
/// Powód: literał typu <c>"2026-08-04"</c> to bomba zegarowa. Booking odrzuca terminy przeszłe,
/// więc dzień po tej dacie `available-slots` zwraca pustą listę, hold dostaje 400 zamiast 200,
/// a test izolacji tenantów 400 zamiast 404 — czyli przestaje sprawdzać izolację i zaczyna
/// sprawdzać kalendarz. W sierpniu 2026 wywróciło to naraz 10 testów; kolejne 50 literałów
/// czekało w kolejce z datami rozsianymi po całym roku.
///
/// Zasady:
///  • <see cref="InDays"/> zachowuje ODSTĘPY między datami — literały 2026-11-10 i 2026-11-11
///    zamieniamy na InDays(30) i InDays(31), więc sąsiedztwo i kolejność zostają nienaruszone.
///  • <see cref="MonthStart"/> jest dla testów operujących na MIESIĄCACH (publikacje, horyzont).
///    Nie licz na to, że InDays(30) i InDays(40) wypadną w różnych miesiącach — zależnie od dnia
///    startu wypadają albo nie, i dokładnie na tym wywrócił się jeden z testów.
///  • Przeszłość wyrażamy ujemnym offsetem (historia, purge), nie literałem sprzed lat.
///
/// Testy potrzebujące konkretnego dnia kalendarza (przejście czasu letniego, rok przestępny)
/// są wyjątkiem i mają prawo do literału — tam data JEST przedmiotem testu. Powinny to mówić
/// komentarzem, żeby nie wyglądały na przeoczenie.
/// </summary>
internal static class TestDates
{
  /// <summary>Dziś w UTC. Domena liczy „dzisiaj" w strefie salonu; do testów UTC wystarcza.</summary>
  public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

  /// <summary>Dzień oddalony o <paramref name="days"/> (ujemne = przeszłość).</summary>
  public static DateOnly InDays(int days) => Today.AddDays(days);

  /// <summary>To samo co <see cref="InDays"/>, w formacie <c>yyyy-MM-dd</c> — do URL-i i JSON-a.</summary>
  public static string IsoInDays(int days) => InDays(days).ToString("yyyy-MM-dd");

  /// <summary>
  /// Pierwszy dzień miesiąca oddalonego o <paramref name="monthsAhead"/>. Dla testów, którym
  /// zależy na GRANICY MIESIĄCA (publikacje miesiąca, horyzont rezerwacji) — daje pewność,
  /// że dwie daty wypadną w różnych miesiącach niezależnie od dnia uruchomienia.
  /// </summary>
  public static DateOnly MonthStart(int monthsAhead) =>
    new DateOnly(Today.Year, Today.Month, 1).AddMonths(monthsAhead);

  /// <summary><see cref="MonthStart"/> w formacie <c>yyyy-MM-dd</c>.</summary>
  public static string IsoMonthStart(int monthsAhead) => MonthStart(monthsAhead).ToString("yyyy-MM-dd");
}
