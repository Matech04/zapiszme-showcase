using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace App.Api.IntegrationTests;

/// <summary>
/// Strażnik: w testach integracyjnych nie wolno zaszywać dat literałem.
///
/// Powód jest empiryczny, nie estetyczny. W sierpniu 2026 przeterminowane literały wywróciły
/// naraz 10 testów, a diagnoza zajęła kilka godzin, bo objaw (puste sloty, 400 zamiast 404)
/// wyglądał jak regresja w kodzie produkcyjnym. W suicie czekało wtedy jeszcze ~50 takich dat
/// rozsianych po całym roku — każda z własną datą wybuchu.
///
/// Groźniejsze od czerwonej suity jest to, że test z przeterminowaną datą potrafi przestać
/// sprawdzać to, co deklaruje: test izolacji tenantów dostawał 400 (termin przeszły) zamiast 404
/// i przechodził dalej, choć izolacji już nie weryfikował.
///
/// Zamiast literału używaj <see cref="TestDates"/>. Jeśli data JEST przedmiotem testu
/// (wstrzyknięty zegar, przejście czasu letniego, rok przestępny), dopisz plik do
/// <see cref="Dozwolone"/> wraz z uzasadnieniem — świadomy wyjątek jest w porządku,
/// przeoczenie nie.
/// </summary>
public sealed class NoHardcodedDatesGuardTests
{
  /// <summary>Literał daty: <c>"2026-08-04"</c>, <c>new DateOnly(2026, 8, 4)</c>, <c>date=2026-08-04</c> w URL-u.</summary>
  private static readonly Regex DateLiteral = new(
    """("\d{4}-\d{2}-\d{2}"|new\s+DateOnly\s*\(\s*\d{4}\s*,|new\s+DateTime\s*\(\s*\d{4}\s*,|=\d{4}-\d{2}-\d{2})""",
    RegexOptions.Compiled);

  /// <summary>Wartownik „bez końca" — stała sentinel, nie data kalendarzowa. Nigdy nie wygasa.</summary>
  private const string SentinelFarFuture = "9999-12-31";

  private static readonly HashSet<string> Dozwolone = new(StringComparer.OrdinalIgnoreCase)
  {
    // Definicja helpera — w dokumentacji cytuje literały jako PRZYKŁADY tego, czego nie robić.
    "TestDates.cs",
    // Ten plik — regexy zawierają wzorce dat.
    "NoHardcodedDatesGuardTests.cs",
    // Czas jest tu WSTRZYKIWANY (`utcNow` idzie wprost do RunCycleAsync), a pozostałe daty są
    // względem niego. Zestaw jest wewnętrznie spójny i niezależny od dnia uruchomienia.
    "AppointmentHistoryPurgeIntegrationTests.cs",
  };

  [Fact]
  public void Testy_integracyjne_nie_zaszywaja_dat_literalem()
  {
    var katalog = KatalogProjektu();
    var znalezione = new List<string>();

    foreach (var plik in Directory.EnumerateFiles(katalog, "*.cs", SearchOption.AllDirectories))
    {
      // `obj/` i `bin/` niosą wygenerowany kod (atrybuty assembly z datami) — nie nasz.
      if (plik.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
          || plik.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
      {
        continue;
      }

      var nazwa = Path.GetFileName(plik);
      if (Dozwolone.Contains(nazwa))
      {
        continue;
      }

      var linie = File.ReadAllLines(plik);
      for (var i = 0; i < linie.Length; i++)
      {
        var linia = linie[i];
        if (linia.Contains(SentinelFarFuture, StringComparison.Ordinal))
        {
          continue;
        }

        var trafienie = DateLiteral.Match(linia);
        if (trafienie.Success)
        {
          znalezione.Add($"{nazwa}:{i + 1}  {trafienie.Value.Trim()}");
        }
      }
    }

    Assert.True(
      znalezione.Count == 0,
      "Zaszyta data w teście — to bomba zegarowa. Użyj TestDates.InDays/IsoInDays/MonthStart, "
      + "albo dopisz plik do listy Dozwolone z uzasadnieniem, jeśli data JEST przedmiotem testu."
      + Environment.NewLine
      + string.Join(Environment.NewLine, znalezione));
  }

  private static string KatalogProjektu([CallerFilePath] string sciezkaTegoPliku = "") =>
    Path.GetDirectoryName(sciezkaTegoPliku)!;
}
