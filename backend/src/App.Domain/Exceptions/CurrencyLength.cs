namespace App.Domain.Exceptions;

public class CurrencyLength : DomainException
{
  public CurrencyLength()
      : base("Kod waluty musi mieć dokładnie 3 znaki.", ErrorCodes.CurrencyInvalidLength)
  {

  }
}