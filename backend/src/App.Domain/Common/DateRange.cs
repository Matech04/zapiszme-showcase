
public record DateRange
{
  public DateOnly StartDate { get; init; }
  public DateOnly EndDate { get; init; }

  public DateRange(DateOnly startDate, DateOnly endDate)
  {
    if (startDate > endDate)
    {
      throw new InvalidDateRangeException();
    }

    StartDate = startDate;
    EndDate = endDate;
  }

  public bool OverlapsWith(DateRange other)
  {
    return StartDate <= other.EndDate && EndDate >= other.StartDate;
  }

  public bool IsContainedIn(DateRange other)
  {
    return StartDate >= other.StartDate && EndDate <= other.EndDate;
  }

  public bool Contains(DateRange other)
  {
    return StartDate <= other.StartDate && EndDate >= other.EndDate;
  }

  public bool Contains(DateOnly date)
  {
    return date >= StartDate && date <= EndDate;
  }
}