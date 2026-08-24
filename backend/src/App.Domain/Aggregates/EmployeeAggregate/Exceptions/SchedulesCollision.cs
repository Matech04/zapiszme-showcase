//exception for schedules collision
using App.Domain.Exceptions;

public class SchedulesCollisionException : DomainException
{
  public SchedulesCollisionException()
      : base("Schedules collision.", ErrorCodes.SchedulesCollision)
  {
  }
}