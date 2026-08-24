using App.Domain.Common;

public class TimeSlot : ITimeSlot
{
  public TimeOnly StartTime { get; init; }
  public TimeOnly EndTime { get; init; }
  public bool IsBreak { get; init; }

  public TimeSlot(TimeOnly startTime, TimeOnly endTime, bool isBreak)
  {
    Guard.AgainstInvalidTimeRange(startTime, endTime);

    StartTime = startTime;
    EndTime = endTime;
    IsBreak = isBreak;
  }
}