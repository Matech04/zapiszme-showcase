namespace App.Domain.Exceptions;

/// <summary>
/// Podano numer telefonu spoza Polski (+48) tam, gdzie ścieżka wysyła realny SMS (rejestracja).
/// Anti toll-fraud: numery zagraniczne/premium są nielegitne dla ICP produktu. Mapowane na 400
/// z zachowaniem czytelnego komunikatu (w przeciwieństwie do generycznego <c>ArgumentException</c>).
/// </summary>
public sealed class UnsupportedPhoneRegionException : DomainException
{
  public UnsupportedPhoneRegionException()
    : base("Obsługujemy wyłącznie polskie numery telefonu (+48).")
  {
  }
}
