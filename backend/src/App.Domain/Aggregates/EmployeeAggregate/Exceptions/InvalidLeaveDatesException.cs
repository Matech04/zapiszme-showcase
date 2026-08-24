using App.Domain.Exceptions;

public class InvalidLeaveDatesException : DomainException // lub Twoja bazowa klasa wyjątków domenowych
{
  public InvalidLeaveDatesException()
      : base("Data rozpoczęcia urlopu nie może być późniejsza niż data zakończenia.", ErrorCodes.LeaveInvalidDates)
  {
  }
}