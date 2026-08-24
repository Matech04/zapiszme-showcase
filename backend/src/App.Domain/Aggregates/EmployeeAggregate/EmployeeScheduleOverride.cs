using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class ScheduleOverride : Entity
{
  public DateOnly Date { get; private set; }
  public ScheduleDay ScheduleDay { get; private set; } = null!;

  public ScheduleOverride(DateOnly date, ScheduleDay scheduleDay)
  {
    Id = Guid.NewGuid();
    Date = date;
    ScheduleDay = scheduleDay;
  }

  private ScheduleOverride() { }

  public IReadOnlyCollection<TimeRange> WorkRanges =>
    ScheduleDay.WorkRanges;

  public IReadOnlyCollection<TimeRange> Breaks =>
    ScheduleDay.Breaks;

  public IReadOnlyCollection<TimeOnly> FixedStartTimes =>
    ScheduleDay.FixedStartTimes;

  /// <summary>Czy ten wyjątek jest zdefiniowany stałymi godzinami startu (a nie przedziałami pracy).</summary>
  public bool IsFixed => ScheduleDay.IsFixed;

  internal void SetScheduleDay(ScheduleDay scheduleDay)
  {
    ScheduleDay = scheduleDay;
  }

  public bool IsRangeAvailable(TimeRange timeRange)
  {
    return ScheduleDay.IsRangeAvailable(timeRange);
  }
}