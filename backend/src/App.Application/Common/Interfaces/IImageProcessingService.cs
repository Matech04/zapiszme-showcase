namespace App.Application.Common.Interfaces;

/// <summary>
/// Pipeline obróbki + zapisu obrazu. Waliduje, że strumień to faktyczny obraz (magic bytes),
/// zdejmuje EXIF, ogranicza wymiary, generuje główny obraz WebP + miniaturę WebP i wgrywa oba
/// przez <see cref="IFileStorage"/>. Zwraca publiczne URL-e.
///
/// To building block współdzielony przez:
///  - staffowy endpoint <c>POST /api/media/images</c> (#5 zdjęcia usług),
///  - anonimowy upload inspiracji w public bookingu (#2) — wstrzykiwany w <c>BookingApiControllerBase</c>.
/// </summary>
public interface IImageProcessingService
{
  /// <summary>
  /// Przetwarza i zapisuje pojedynczy obraz pod danym prefiksem klucza (np. <c>services</c>,
  /// <c>inspirations</c>). Rzuca <see cref="App.Domain.Exceptions.InvalidImageException"/> gdy
  /// wejście nie jest obsługiwanym obrazem lub przekracza limit rozmiaru.
  /// </summary>
  /// <param name="content">Surowy strumień przesłanego pliku.</param>
  /// <param name="keyPrefix">Prefiks (folder) klucza — kontrolowany przez serwer, nie usera.</param>
  Task<ProcessedImageResult> ProcessAndStoreAsync(
    Stream content,
    string keyPrefix,
    CancellationToken ct = default);
}

/// <summary>
/// Wynik obróbki: klucze obiektów (główny + miniatura) i ich publiczne URL-e. Oba klucze są
/// niezależnymi obiektami w storage — trzeba je persystować, by móc je później skasować (np. po
/// przejściu wizyty w stan terminalny), inaczej miniatura zostaje sierotą w buckecie.
/// </summary>
public sealed record ProcessedImageResult(string Key, string Url, string ThumbnailUrl, string ThumbnailKey);
