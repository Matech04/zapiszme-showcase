using App.Domain.Exceptions;

public class OnlyUpcomingLeaveMayBeRemovedException : DomainException
{
  public OnlyUpcomingLeaveMayBeRemovedException()
      : base(
          "Można usunąć tylko nadchodzący urlop (data rozpoczęcia jest w przyszłości).",
          ErrorCodes.LeaveOnlyUpcomingMayBeRemoved)
  {
  }
}
