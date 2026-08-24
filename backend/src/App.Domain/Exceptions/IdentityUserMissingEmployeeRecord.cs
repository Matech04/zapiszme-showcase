namespace App.Domain.Exceptions;

/// <summary>
/// Zalogowany użytkownik Identity nie ma aktywnego rekordu Employee.
/// </summary>
public class IdentityUserMissingEmployeeRecord : DomainException
{
  public IdentityUserMissingEmployeeRecord()
    : base(
        "Konto użytkownika nie jest powiązane z pracownikiem w systemie. Skontaktuj się z administratorem.",
        ErrorCodes.IdentityEmployeeMissing)
  {
  }
}
