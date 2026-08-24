namespace App.Domain.Exceptions;

public class MaxLength : DomainException
{
  public MaxLength(decimal value)
      : base($"Wartość nie może być większa niż {value}.", ErrorCodes.ValueTooLong)
  {

  }
}