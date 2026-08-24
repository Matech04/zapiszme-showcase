namespace App.Domain.Exceptions;

public class AppointmentBookingRuleException : DomainException
{
  public AppointmentBookingRuleException(string message, string errorCode = ErrorCodes.InvalidArgument)
      : base(message, errorCode)
  {
  }
}
