//exception for schedule days collision
using App.Domain.Exceptions;

public class ScheduleDaysCollisionException : DomainException
{
  public ScheduleDaysCollisionException()
      : base("Schedule days collision.", ErrorCodes.ScheduleDaysCollision)
  {
  }
}