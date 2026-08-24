using App.Domain.Exceptions;

public class InvalidDateRangeException : DomainException
{
  public InvalidDateRangeException()
      : base("Data zakończenia musi być późniejsza niż data rozpoczęcia.", ErrorCodes.InvalidDateRange)
  {
  }
}