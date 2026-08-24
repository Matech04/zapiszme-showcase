using App.Domain.Common;
using App.Domain.Exceptions;

public record Money
{
  public decimal Amount { get; init; }
  public string Currency { get; init; } = string.Empty;

  public Money(decimal amount, string currency = "PLN")
  {
    Guard.AgainstNegative(amount, nameof(amount));
    Guard.AgainstEmptyString(currency, nameof(currency));
    currency = currency.ToUpper();

    if (currency.Length != 3)
    {
      throw new CurrencyLength();
    }

    Amount = amount;
    Currency = currency;
  }

  public static Money Zero(string currency = "PLN") => new(0, currency);
}