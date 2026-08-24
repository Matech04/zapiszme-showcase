namespace App.Application.Common.Interfaces;

/// <summary>
/// Storage-agnostyczna warstwa przechowywania obiektów (obrazów). Implementacja produkcyjna mówi
/// protokołem S3 (Cloudflare R2); lokalnie ten sam kod celuje w MinIO (S3-compatible) — przełączane
/// configiem (sekcja <c>Storage</c>). FUNDAMENT dla featurów zdjęć usług (#5) i inspiracji klientek (#2).
///
/// KLUCZE OBIEKTÓW generuje warstwa wyżej (pipeline obróbki) losowo (Guid) z prefiksem
/// (np. <c>services/{guid}.webp</c>) — NIGDY nie używaj user-controlled ścieżek jako klucza
/// (path traversal / nadpisanie cudzych obiektów).
/// </summary>
public interface IFileStorage
{
  /// <summary>
  /// Wgrywa zawartość pod podany klucz i zwraca publiczny URL do odczytu.
  /// </summary>
  /// <param name="content">Strumień z gotową (już przetworzoną) zawartością obiektu.</param>
  /// <param name="key">Pełny klucz obiektu w buckecie (np. <c>services/{guid}.webp</c>).</param>
  /// <param name="contentType">MIME odpowiedzi przy odczycie (np. <c>image/webp</c>).</param>
  Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default);

  /// <summary>Usuwa obiekt o danym kluczu. Idempotentne — brak obiektu nie jest błędem.</summary>
  Task DeleteAsync(string key, CancellationToken ct = default);

  /// <summary>Buduje publiczny URL odczytu dla klucza bez wykonywania I/O (np. do zapisu w encji).</summary>
  string BuildPublicUrl(string key);
}
