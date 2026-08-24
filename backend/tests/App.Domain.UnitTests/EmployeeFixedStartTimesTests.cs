using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

namespace App.Domain.UnitTests;

/// <summary>
/// Rozwiązywanie stałych slotów per dzień (<see cref="Employee.GetFixedStartTimes"/>) odwzorowuje
/// kolejność <see cref="Employee.GetWorkingRanges"/>: urlop → pusto; override → godziny z override;
/// inaczej dzień bazowy. W trybie fixed <see cref="Employee.IsAvailable"/> jest permisywne (poza urlopem).
/// </summary>
public class EmployeeFixedStartTimesTests
{
  private static TimeOnly T(int h, int m = 0) => new(h, m);

  private static Employee CreateFixedEmployee()
  {
    var e = new Employee(Guid.NewGuid(), Guid.NewGuid(), "Ola", "Lashes", "ola@lashes.local");
    e.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    return e;
  }

  // cycleIndex dla pojedynczego tygodnia = (int)DayOfWeek (Sunday=0, Monday=1, ...).
  private static void SetFixedDay(Employee e, DayOfWeek day, params TimeOnly[] times)
  {
    var days = new List<ScheduleDay> { new(times, cycleIndex: (int)day) };
    e.SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days);
  }

  [Fact]
  public void GetFixedStartTimes_returns_base_schedule_day_times()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2); // poniedziałek
    SetFixedDay(e, DayOfWeek.Monday, T(9), T(12), T(15));

    Assert.Equal(new[] { T(9), T(12), T(15) }, e.GetFixedStartTimes(monday));
  }

  [Fact]
  public void GetFixedStartTimes_returns_empty_on_day_without_schedule()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2);
    SetFixedDay(e, DayOfWeek.Monday, T(9));

    var tuesday = monday.AddDays(1);
    Assert.Empty(e.GetFixedStartTimes(tuesday));
  }

  [Fact]
  public void GetFixedStartTimes_returns_empty_on_leave()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2);
    SetFixedDay(e, DayOfWeek.Monday, T(9), T(12));
    e.AddLeave(monday, monday);

    Assert.Empty(e.GetFixedStartTimes(monday));
  }

  [Fact]
  public void GetFixedStartTimes_uses_override_when_present()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2);
    SetFixedDay(e, DayOfWeek.Monday, T(9), T(12));
    e.SetScheduleOverride(monday, new ScheduleDay(new[] { T(11) }));

    Assert.Equal(new[] { T(11) }, e.GetFixedStartTimes(monday));
  }

  [Fact]
  public void IsAvailable_fixed_mode_is_permissive_outside_leave()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2);
    SetFixedDay(e, DayOfWeek.Monday, T(9));

    // Panel: dowolna godzina dozwolona — nawet poza zdefiniowanymi slotami (egzekucja online żyje w komendzie).
    Assert.True(e.IsAvailable(new TimeRange(T(13), T(14)), monday));
  }

  [Fact]
  public void IsAvailable_fixed_mode_false_on_leave()
  {
    var e = CreateFixedEmployee();
    var monday = new DateOnly(2026, 2, 2);
    SetFixedDay(e, DayOfWeek.Monday, T(9));
    e.AddLeave(monday, monday);

    Assert.False(e.IsAvailable(new TimeRange(T(9), T(10)), monday));
  }
}
