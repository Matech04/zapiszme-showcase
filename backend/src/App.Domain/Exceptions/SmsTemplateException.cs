namespace App.Domain.Exceptions;

/// <summary>
/// Błąd reguł biznesowych custom szablonu SMS (typ niepersonalizowalny, treść za długa wg kodowania,
/// niedozwolony placeholder, brak treści oczekującej). Niesie własny <see cref="ErrorCode"/>, żeby
/// front mapował go na konkretny PL komunikat.
/// </summary>
public sealed class SmsTemplateException : DomainException
{
  public SmsTemplateException(string message, string errorCode) : base(message, errorCode) { }
}
