namespace App.Domain.Exceptions;

public class BreakNotWithinWorkRange : DomainException
{
  public BreakNotWithinWorkRange()
      : base("Break not within work range.", ErrorCodes.ScheduleBreakNotWithinWorkRange)
  {
  }
}