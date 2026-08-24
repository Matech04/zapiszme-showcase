namespace App.Domain.Exceptions;

/// <summary>
/// Próba przypisania white-label domeny, która jest już używana przez innego tenanta. Host→tenant
/// musi być 1:1 (egzekwowane też partial unique index na <c>custom_domain</c>) — ten wyjątek daje
/// czytelny błąd zanim uderzymy w bazę.
/// </summary>
public sealed class CustomDomainAlreadyAssignedException : DomainException
{
  public CustomDomainAlreadyAssignedException(string domain)
    : base($"Domena '{domain}' jest już przypisana do innego salonu.", ErrorCodes.CustomDomainAlreadyAssigned)
  {
  }
}
