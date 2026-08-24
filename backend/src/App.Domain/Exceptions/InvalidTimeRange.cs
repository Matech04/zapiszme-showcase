using App.Domain.Exceptions;

public class InvalidTimeRangeException : DomainException
{
  public InvalidTimeRangeException()
      : base("Godzina zakończenia musi być późniejsza niż godzina rozpoczęcia.", ErrorCodes.AppointmentInvalidTimeRange)
  {
  }
}