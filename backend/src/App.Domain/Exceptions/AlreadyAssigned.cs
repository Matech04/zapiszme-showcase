namespace App.Domain.Exceptions;

public class AlreadyAssigned : DomainException
{
  public AlreadyAssigned(string name)
      : base($"Ta usługa jest już przypisana ({name}).", ErrorCodes.EmployeeServiceAlreadyAssigned)
  {
  }
}