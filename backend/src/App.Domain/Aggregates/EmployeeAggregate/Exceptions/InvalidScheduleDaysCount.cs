//Excpetion for invalid schedule days count
using App.Domain.Exceptions;

public class InvalidScheduleDaysCountException : DomainException
{
  public InvalidScheduleDaysCountException()
      : base("Invalid schedule days count.", ErrorCodes.ScheduleInvalidDaysCount)
  {
  }
}