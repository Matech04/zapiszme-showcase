using App.Domain.Exceptions;

public class OverlappingShiftsException : DomainException
{
  public OverlappingShiftsException()
      : base("Przedziały godzin nakładają się na siebie.", ErrorCodes.ScheduleOverlappingShifts)
  {
  }
}