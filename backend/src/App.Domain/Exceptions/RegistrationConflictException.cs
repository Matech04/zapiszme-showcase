namespace App.Domain.Exceptions;

/// <summary>
/// Rejestracja odrzucona — email LUB telefon już są w użyciu. Świadomie generyczny komunikat
/// (bez wskazywania pola), żeby nie udostępniać enumeracji którego z dwóch pól dotyczy konflikt.
/// </summary>
public sealed class RegistrationConflictException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.RegistrationConflict;

  public RegistrationConflictException()
    : base("Nie udało się utworzyć konta. Sprawdź wprowadzone dane.")
  {
  }
}
