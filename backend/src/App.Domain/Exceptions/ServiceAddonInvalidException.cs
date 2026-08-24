namespace App.Domain.Exceptions;

/// <summary>
/// Rzucane gdy lista dodatków usługi głównej zawiera nieprawidłową pozycję — usługę, która nie
/// istnieje w tenancie albo nie jest oznaczona jako dodatek (<c>IsAddon = false</c>).
/// </summary>
public class ServiceAddonInvalidException : DomainException
{
  public ServiceAddonInvalidException(string message)
    : base(message, ErrorCodes.ServiceAddonInvalid)
  {
  }
}
