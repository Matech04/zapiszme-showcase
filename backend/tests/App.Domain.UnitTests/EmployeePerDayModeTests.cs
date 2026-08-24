using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

namespace App.Domain.UnitTests;

/// <summary>
/// Tryb generowania slotów rozstrzygany PER DZIEŃ: jeśli na dany dzień jest override, decyduje
/// JEGO tryb (kosmetyczka może budować grafik samymi „klikniętymi" dniami, bez cyklu tygodniowego,
/// i mieszać tryby per dzień). Bez override'a — globalny tryb pracownika.
/// </summary>
public class EmployeePerDayModeTests
{
  private static TimeOnly T(int h, int m = 0) => new(h, m);
  private static readonly DateOnly Monday = new(2026, 2, 2);
  private static readonly DateOnly Tuesday = new(2026, 2, 3);

  private static Employee Grid() =>
    new(Guid.NewGuid(), Guid.NewGuid(), "Ala", "Grid", "ala@grid.local"); // domyślnie Grid

  private static Employee Fixed()
  {
    var e = new Employee(Guid.NewGuid(), Guid.NewGuid(), "Ola", "Fixed", "ola@fixed.local");
    e.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    return e;
  }

  [Fact]
  public void No_weekly_schedule_fixed_override_makes_that_day_fixed()
  {
    // Scenariusz „papierowego kalendarza": brak grafiku tygodniowego, sam wyjątek stały na dzień.
    var e = Grid(); // globalnie Grid, ale to nieistotne dla dnia z override'em
    e.SetScheduleOverride(Monday, new ScheduleDay(new[] { T(9), T(12), T(15) }));

    Assert.True(e.IsFixedForDate(Monday));
    Assert.Equal(SlotGenerationMode.FixedStartTimes, e.GetSlotModeForDate(Monday));
    Assert.Equal(new[] { T(9), T(12), T(15) }, e.GetFixedStartTimes(Monday));
  }

  [Fact]
  public void No_weekly_schedule_grid_override_makes_that_day_grid()
  {
    var e = Fixed(); // globalnie Fixed
    e.SetScheduleOverride(Monday, new ScheduleDay(new[] { new TimeRange(T(10), T(14)) }, null));

    Assert.False(e.IsFixedForDate(Monday));
    Assert.Equal(SlotGenerationMode.Grid, e.GetSlotModeForDate(Monday));
    var r = Assert.Single(e.GetWorkingRanges(Monday));
    Assert.Equal(T(10), r.StartTime);
    Assert.Equal(T(14), r.EndTime);
  }

  [Fact]
  public void Day_without_override_falls_back_to_global_mode()
  {
    var e = Fixed();
    e.SetScheduleOverride(Monday, new ScheduleDay(new[] { new TimeRange(T(10), T(14)) }, null)); // grid override pn

    // wtorek bez override'a → globalny tryb (Fixed)
    Assert.True(e.IsFixedForDate(Tuesday));
    Assert.Equal(SlotGenerationMode.FixedStartTimes, e.GetSlotModeForDate(Tuesday));
  }

  [Fact]
  public void Getters_do_not_cross_read_the_other_modes_override()
  {
    // GetFixedStartTimes czyta tylko override stały; GetWorkingRanges tylko siatkowy.
    var e = Grid();
    e.SetScheduleOverride(Monday, new ScheduleDay(new[] { T(9), T(12) })); // override stały

    Assert.Equal(new[] { T(9), T(12) }, e.GetFixedStartTimes(Monday)); // właściwy getter widzi
    Assert.Empty(e.GetWorkingRanges(Monday));                          // getter siatkowy nie miesza
  }

  [Fact]
  public void Fixed_mode_day_is_permissive_in_IsAvailable_even_via_override()
  {
    var e = Grid();
    e.SetScheduleOverride(Monday, new ScheduleDay(new[] { T(9) })); // override stały → dzień fixed

    // tryb stały = permisywny w panelu (egzekucja „tylko sloty" dotyczy online w komendzie)
    Assert.True(e.IsAvailable(new TimeRange(T(13), T(14)), Monday));
  }

  // Regresja: pracownik z DWOMA grafikami o różnych trybach (lipiec statyczny, sierpień z przedziałami).
  // Globalny SlotGenerationMode odzwierciedla tylko OSTATNIO zapisany grafik, więc tryb dnia MUSI
  // wynikać z faktycznego dnia grafiku obowiązującego dla daty — inaczej dodawanie wizyty w drugim
  // grafiku widzi zły tryb i „brak wolnych terminów".
  private static readonly DateOnly JulyWed = new(2026, 7, 8);   // środa w grafiku statycznym
  private static readonly DateOnly AugustWed = new(2026, 8, 5); // środa w grafiku z przedziałami

  private static Employee WithJulyFixedAndAugustGrid(SlotGenerationMode globalMode)
  {
    var e = Grid();
    e.SetSlotGenerationMode(globalMode); // symuluje „ostatnio zapisany grafik" ustawiający tryb globalny

    // Lipiec: grafik statyczny (dzień „fixed" ma godziny startu, brak WorkRanges). Środa = DayOfWeek 3.
    e.AddSchedule(
      new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
      1,
      new[] { new ScheduleDay(new[] { T(9), T(12), T(15) }, cycleIndex: 3) });

    // Sierpień: grafik z przedziałami (Grid). Środa = DayOfWeek 3.
    e.AddSchedule(
      new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
      1,
      new[] { new ScheduleDay(new[] { new TimeRange(T(10), T(14)) }, null, cycleIndex: 3) });

    return e;
  }

  [Theory]
  [InlineData(SlotGenerationMode.Grid)]           // „sierpień zapisany ostatni" → globalny Grid
  [InlineData(SlotGenerationMode.FixedStartTimes)] // „lipiec zapisany ostatni" → globalny Fixed
  public void Per_date_mode_follows_the_schedule_active_for_that_date_not_global(SlotGenerationMode globalMode)
  {
    var e = WithJulyFixedAndAugustGrid(globalMode);

    // Lipiec liczy się jako STATYCZNY niezależnie od trybu globalnego.
    Assert.True(e.IsFixedForDate(JulyWed));
    Assert.Equal(new[] { T(9), T(12), T(15) }, e.GetFixedStartTimes(JulyWed));

    // Sierpień liczy się jako PRZEDZIAŁY niezależnie od trybu globalnego.
    Assert.False(e.IsFixedForDate(AugustWed));
    var r = Assert.Single(e.GetWorkingRanges(AugustWed));
    Assert.Equal(T(10), r.StartTime);
    Assert.Equal(T(14), r.EndTime);
  }
}
