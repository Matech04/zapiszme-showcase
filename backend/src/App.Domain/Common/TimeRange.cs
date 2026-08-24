namespace App.Domain.Common;

public record TimeRange
{
  public TimeOnly StartTime { get; init; }
  public TimeOnly EndTime { get; init; }

  public TimeRange(TimeOnly startTime, TimeOnly endTime)
  {

    Guard.AgainstInvalidTimeRange(startTime, endTime);
    StartTime = startTime;
    EndTime = endTime;
  }

  public bool OverlapsWith(TimeRange other)
  {
    return StartTime < other.EndTime && other.StartTime < EndTime;
  }

  public bool IsContainedIn(TimeRange other)
  {
    return StartTime >= other.StartTime && EndTime <= other.EndTime;
  }

  // Sprawdza czy TEN przedział (this) wchłania/zawiera inny (other)
  // Czytamy: scheduleRange.Contains(appointmentRange)
  public bool Contains(TimeRange other)
  {
    return StartTime <= other.StartTime && EndTime >= other.EndTime;
  }

  public static List<TimeRange> ValidateRanges(IEnumerable<TimeRange> ranges)
  {
    if (ranges == null || !ranges.Any()) throw new ArgumentNullException(nameof(ranges));

    // Sortujemy raz, aby sprawdzanie było wydajne (O(n log n))
    var sorted = ranges.OrderBy(r => r.StartTime).ToList();

    for (int i = 0; i < sorted.Count - 1; i++)
    {
      // Jeśli koniec obecnego zakresu jest po rozpoczęciu następnego -> mamy overlap
      if (sorted[i].EndTime > sorted[i + 1].StartTime)
      {
        throw new OverlappingShiftsException();
      }
    }

    return sorted;
  }

}