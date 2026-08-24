public interface ITimeSlot
{
  TimeOnly StartTime { get; init; }
  TimeOnly EndTime { get; init; }
  bool IsBreak { get; init; }
}