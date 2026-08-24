namespace App.Domain.Exceptions;

public class NotFoundException : Exception, IErrorCodeException
{
  public NotFoundException(string name, object key) : base($"Nie znaleziono zasobu {name}.")
  {
    ResourceName = name;
    ResourceKey = key;
  }

  public string ErrorCode => ErrorCodes.NotFound;

  public string ResourceName { get; }

  public object ResourceKey { get; }
}