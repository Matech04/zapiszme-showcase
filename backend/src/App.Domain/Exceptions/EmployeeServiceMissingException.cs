namespace App.Domain.Exceptions;

public sealed class EmployeeServiceMissingException : DomainException
{
  public EmployeeServiceMissingException()
      : base("Pracownik nie ma przypisanej tej usługi.", ErrorCodes.EmployeeServiceMissing)
  {
  }
}
