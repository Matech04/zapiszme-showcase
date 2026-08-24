namespace App.Domain.Exceptions;

public class TenantViolation : Exception
{
  public TenantViolation() : base($"Tenant Id Violation")
  {

  }
}