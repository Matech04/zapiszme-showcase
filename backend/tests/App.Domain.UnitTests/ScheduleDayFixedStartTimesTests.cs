using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Domain.UnitTests;

/// <summary>
/// Konstruktor dnia w trybie stałych slotów: normalizuje godziny (sort + dedupe), nie ustawia
/// przedziałów pracy ani przerw, oznacza dzień jako <see cref="ScheduleDay.IsFixed"/>.
/// </summary>
public class ScheduleDayFixedStartTimesTests
{
  private static TimeOnly T(int h, int m = 0) => new(h, m);

  [Fact]
  public void FixedConstructor_sorts_and_dedupes_times()
  {
    var day = new ScheduleDay(new[] { T(12), T(9), T(9), T(15) }, cycleIndex: 1);

    Assert.Equal(new[] { T(9), T(12), T(15) }, day.FixedStartTimes);
    Assert.True(day.IsFixed);
    Assert.Equal(1, day.CycleIndex);
  }

  [Fact]
  public void FixedConstructor_leaves_workranges_and_breaks_empty()
  {
    var day = new ScheduleDay(new[] { T(9), T(12) });

    Assert.Empty(day.WorkRanges);
    Assert.Empty(day.Breaks);
  }

  [Fact]
  public void FixedConstructor_rejects_empty_list()
  {
    Assert.Throws<ArgumentException>(() => new ScheduleDay(Array.Empty<TimeOnly>(), cycleIndex: 0));
  }

  [Fact]
  public void GridConstructor_is_not_fixed()
  {
    var day = new ScheduleDay(new[] { new App.Domain.Common.TimeRange(T(9), T(17)) }, cycleIndex: 1);

    Assert.False(day.IsFixed);
    Assert.Empty(day.FixedStartTimes);
  }
}
