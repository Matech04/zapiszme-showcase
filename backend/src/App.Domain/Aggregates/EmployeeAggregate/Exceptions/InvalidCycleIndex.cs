//exception for invalid cycle index
using App.Domain.Exceptions;

public class InvalidCycleIndexException : DomainException
{
  public InvalidCycleIndexException()
      : base("Invalid cycle index.", ErrorCodes.ScheduleInvalidCycleIndex)
  {
  }
}