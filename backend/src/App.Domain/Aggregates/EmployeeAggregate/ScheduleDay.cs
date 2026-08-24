using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class ScheduleDay : Entity
{
  public int? CycleIndex { get; private set; }

  private readonly List<TimeRange> _workRanges = new();
  public IReadOnlyCollection<TimeRange> WorkRanges => _workRanges.AsReadOnly();

  private readonly List<TimeRange> _breaks = new();
  public IReadOnlyCollection<TimeRange> Breaks => _breaks.AsReadOnly();

  // Tryb stałych slotów: pracownik wpisuje SAME godziny startu (sloty są grafikiem).
  // Wypełnione tylko gdy dzień należy do pracownika w trybie FixedStartTimes; wtedy WorkRanges/Breaks są puste.
  private List<TimeOnly> _fixedStartTimes = new();
  public IReadOnlyCollection<TimeOnly> FixedStartTimes => _fixedStartTimes.AsReadOnly();

  /// <summary>Czy ten dzień jest zdefiniowany przez stałe sloty (a nie przedziały pracy).</summary>
  public bool IsFixed => _fixedStartTimes.Count > 0;

  public ScheduleDay(IEnumerable<TimeRange> workRanges, IEnumerable<TimeRange>? breaks = null, int? cycleIndex = null)
  {
    Id = Guid.NewGuid();

    if (cycleIndex != null)
    {
      Guard.AgainstInvalidRange(cycleIndex.Value, 0, 27, nameof(cycleIndex));
      CycleIndex = cycleIndex;
    }

    SetWorkRanges(workRanges);

    if (breaks != null)
    {
      UpdateBreaks(breaks);
    }
  }

  /// <summary>
  /// Konstruktor dnia w trybie stałych slotów — bez przedziałów pracy i przerw, tylko lista godzin startu.
  /// </summary>
  public ScheduleDay(IEnumerable<TimeOnly> fixedStartTimes, int? cycleIndex = null)
  {
    Id = Guid.NewGuid();

    if (cycleIndex != null)
    {
      Guard.AgainstInvalidRange(cycleIndex.Value, 0, 27, nameof(cycleIndex));
      CycleIndex = cycleIndex;
    }

    SetFixedStartTimes(fixedStartTimes);
  }

  private ScheduleDay() { }

  internal void SetFixedStartTimes(IEnumerable<TimeOnly> fixedStartTimes)
  {
    if (fixedStartTimes == null)
    {
      throw new ArgumentNullException(nameof(fixedStartTimes));
    }

    var sorted = fixedStartTimes.Distinct().OrderBy(t => t).ToList();
    if (sorted.Count == 0)
    {
      throw new ArgumentException("FixedStartTimes cannot be empty.", nameof(fixedStartTimes));
    }

    _fixedStartTimes = sorted;
  }

  internal void SetWorkRanges(IEnumerable<TimeRange> workRanges)
  {
    if (workRanges == null)
    {
      throw new ArgumentNullException(nameof(workRanges));
    }

    var ranges = workRanges.ToList();
    if (ranges.Count == 0)
    {
      throw new ArgumentException("WorkRanges cannot be empty.", nameof(workRanges));
    }

    var validatedRanges = TimeRange.ValidateRanges(ranges);

    if (_breaks.Count > 0 && _breaks.Any(b => !validatedRanges.Any(r => r.Contains(b))))
    {
      throw new BreakNotWithinWorkRange();
    }

    _workRanges.Clear();
    _workRanges.AddRange(validatedRanges);
  }

  internal void UpdateBreaks(IEnumerable<TimeRange> breaks)
  {
    if (breaks == null)
    {
      throw new ArgumentNullException(nameof(breaks));
    }

    var breakList = breaks.ToList();
    if (breakList.Count == 0)
    {
      _breaks.Clear();
      return;
    }

    var validatedBreaks = TimeRange.ValidateRanges(breakList);
    if (validatedBreaks.Any(b => !_workRanges.Any(r => r.Contains(b))))
    {
      throw new BreakNotWithinWorkRange();
    }

    _breaks.Clear();
    _breaks.AddRange(validatedBreaks);
  }

  public bool IsRangeAvailable(TimeRange timeRange)
  {
    return _workRanges.Any(r => r.Contains(timeRange))
      && !_breaks.Any(b => b.OverlapsWith(timeRange));
  }

  public List<TimeRange> GetAvailableWorkRanges()
  {
    var result = new List<TimeRange>();

    foreach (var workRange in _workRanges.OrderBy(r => r.StartTime))
    {
      var currentStart = workRange.StartTime;
      var breaksInWorkRange = _breaks
        .Where(b => workRange.Contains(b))
        .OrderBy(b => b.StartTime);

      foreach (var br in breaksInWorkRange)
      {
        if (currentStart < br.StartTime)
        {
          result.Add(new TimeRange(currentStart, br.StartTime));
        }

        currentStart = br.EndTime;
      }

      if (currentStart < workRange.EndTime)
      {
        result.Add(new TimeRange(currentStart, workRange.EndTime));
      }
    }

    return result;
  }
}
