namespace App.Domain.Exceptions;

public class NoTenantHeader : Exception
{
  public NoTenantHeader() : base("Tenant could not be resolved (JWT + employee / middleware).")
  {

  }
}