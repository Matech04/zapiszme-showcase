using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>Odpowiedź uploadu obrazu — publiczne URL-e + klucz obiektu w storage.</summary>
public sealed record UploadImageResponse(string Url, string ThumbnailUrl, string Key);

/// <summary>
/// Generyczny upload obrazów dla panelu (staff). Przyjmuje multipart, przepuszcza przez pipeline
/// obróbki (walidacja magic-bytes, EXIF strip, resize, WebP + miniatura) i zapisuje przez storage.
/// Używane przez #5 (zdjęcia usług). Autoryzacja: <c>GeneralAccess</c> (każdy zalogowany pracownik).
///
/// UWAGA limit body: globalny limit Kestrela to 1 MB (Program.cs). Multipart + większy limit
/// włączamy TYLKO tutaj, per-endpoint (<see cref="RequestSizeLimitAttribute"/> +
/// <see cref="RequestFormLimitsAttribute"/>) — globalnie podnosić NIE wolno.
///
/// ANONIMOWA ścieżka (#2 inspiracje klientek w public bookingu): NIE jest tu budowana. Building
/// blocks gotowe: wstrzyknij <see cref="IImageProcessingService"/> w BookingApiControllerBase i użyj
/// rate-limit policy "PublicImageUpload" (zob. Program.cs / appsettings).
/// </summary>
[Authorize(Policy = "GeneralAccess")]
public class MediaController : ApiControllerBase
{
  // ~6 MB body cap: obraz max 5 MB (ImageProcessing.MaxInputBytes) + narzut multipart boundary/nagłówków.
  private const int MaxUploadBytes = 6 * 1024 * 1024;

  private readonly IImageProcessingService _imageProcessing;

  public MediaController(IImageProcessingService imageProcessing)
  {
    _imageProcessing = imageProcessing;
  }

  [HttpPost("images")]
  [RequestSizeLimit(MaxUploadBytes)]
  [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
  public async Task<ActionResult<UploadImageResponse>> UploadImage(
    IFormFile file,
    CancellationToken ct)
  {
    if (file is null || file.Length == 0)
    {
      throw new InvalidImageException("Nie przesłano pliku.");
    }

    await using var stream = file.OpenReadStream();
    var result = await _imageProcessing.ProcessAndStoreAsync(stream, "services", ct);

    return Ok(new UploadImageResponse(result.Url, result.ThumbnailUrl, result.Key));
  }
}
