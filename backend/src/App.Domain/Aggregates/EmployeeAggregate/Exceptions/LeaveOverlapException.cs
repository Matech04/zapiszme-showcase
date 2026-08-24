using App.Domain.Exceptions;

public class LeaveOverlapException : DomainException // lub Twoja bazowa klasa wyjątków domenowych
{
  public LeaveOverlapException()
      : base("Urlopy pracownika nie mogą się nakładać.", ErrorCodes.LeaveOverlap)
  {
  }
}