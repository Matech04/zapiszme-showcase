namespace App.Domain.Exceptions;

/// <summary>
/// Rzucany, gdy przesłany plik nie jest obsługiwanym obrazem (weryfikacja po magic bytes),
/// przekracza dozwolony rozmiar lub ma nieobsługiwany format. Mapuje się na HTTP 400
/// (przez <c>DomainException</c> w GlobalFallbackExceptionHandler).
/// </summary>
public sealed class InvalidImageException : DomainException
{
  public InvalidImageException(string message, string errorCode = ErrorCodes.ImageInvalid)
      : base(message, errorCode)
  {
  }
}
