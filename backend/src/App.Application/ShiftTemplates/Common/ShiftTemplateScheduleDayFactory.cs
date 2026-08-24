using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

namespace App.Application.ShiftTemplates.Common;

/// <summary>
/// Buduje <see cref="ScheduleDay"/> dla szablonu zależnie od trybu: stałe godziny (lista godzin startu)
/// albo siatka (bloki pracy + przerwy). Wspólne dla komend Create i Update.
/// </summary>
internal static class ShiftTemplateScheduleDayFactory
{
  public static ScheduleDay Build(
    SlotGenerationMode mode,
    IReadOnlyCollection<TimeRangeDto> workRanges,
    IReadOnlyCollection<TimeRangeDto> breaks,
    IReadOnlyCollection<TimeOnly>? fixedStartTimes)
  {
    if (mode == SlotGenerationMode.FixedStartTimes)
    {
      var times = (fixedStartTimes ?? Array.Empty<TimeOnly>()).Distinct().OrderBy(t => t).ToList();
      return new ScheduleDay(times);
    }

    var ranges = workRanges.Select(r => new TimeRange(r.StartTime, r.EndTime)).ToList();
    var breakRanges = breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList();
    return new ScheduleDay(ranges, breakRanges);
  }
}
