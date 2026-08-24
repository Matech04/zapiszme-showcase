namespace App.Domain.Exceptions;

/// <summary>
/// Publiczny adres (slug) salonu jest już zajęty przez innego tenanta. Rzucane przy dokończeniu
/// profilu w kreatorze onboardingu (krok „Nazwa salonu + link"). Mapowane na 409.
/// </summary>
public sealed class SalonSlugTakenException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.SalonSlugTaken;

  public SalonSlugTakenException()
    : base("Ten publiczny adres salonu jest już zajęty. Wybierz inny.")
  {
  }
}
