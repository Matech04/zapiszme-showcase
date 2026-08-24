using App.Application.Booking.BookingAppointments.Commands;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

/// <summary>Odpowiedź uploadu inspiracji — publiczne URL-e + klucz obiektu w storage.</summary>
public sealed record UploadInspirationResponse(string Url, string ThumbnailUrl, string Key);

/// <summary>
/// Upload pojedynczego zdjęcia inspiracji do POTWIERDZONEJ wizyty (#2, deferred-upload). Klientka
/// trzyma zdjęcia w przeglądarce przez cały lejek rezerwacji; dopiero po potwierdzeniu OTP front
/// uploaduje je tutaj, autoryzując żądanie krótkożyjącym tokenem grantu (nagłówek
/// <c>X-Inspiration-Upload-Token</c>) wydanym przez verify-otp / confirm-with-session.
///
/// Dlaczego tak (zero sierot + brak abuse'u):
///  - obiekt trafia na storage WYŁĄCZNIE dla potwierdzonej wizyty (token wydawany dopiero po OTP),
///    więc nie ma otwartego, anonimowego endpointu do zaśmiecania bucketa,
///  - twardy cap liczby zdjęć (3) i walidacja tokenu są w handlerze PRZED obróbką/wgraniem obrazu,
///  - rate-limit <c>PublicImageUpload</c> (10 req/min/IP) + per-endpoint cap rozmiaru body ~6 MB
///    (NIE podnosimy globalnego 1 MB Kestrela).
///
/// Slug w trasie ustawia tenanta przez middleware (jak pozostałe /api/booking/{slug}/...); handler
/// dodatkowo sprawdza, że token grantu został wydany dokładnie dla tej wizyty i że wizyta należy do
/// tenanta ze slugu (defense-in-depth).
/// </summary>
[AllowAnonymous]
[Route("api/booking/{slug}/appointments/{appointmentId:guid}/inspirations")]
public class BookingInspirationsController : BookingApiControllerBase
{
  // ~6 MB body cap: obraz max 5 MB (ImageProcessing.MaxInputBytes) + narzut multipart boundary/nagłówków.
  private const int MaxUploadBytes = 6 * 1024 * 1024;

  [EnableRateLimiting("PublicImageUpload")]
  [HttpPost(Name = "PublicAttachInspiration")]
  [RequestSizeLimit(MaxUploadBytes)]
  [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
  public async Task<ActionResult<UploadInspirationResponse>> AttachInspiration(
    [FromRoute] string slug,
    [FromRoute] Guid appointmentId,
    [FromHeader(Name = "X-Inspiration-Upload-Token")] string? uploadToken,
    IFormFile file,
    CancellationToken ct)
  {
    _ = slug;

    if (file is null || file.Length == 0)
    {
      throw new InvalidImageException("Nie przesłano pliku.");
    }

    await using var stream = file.OpenReadStream();
    var result = await Mediator.Send(
      new AttachInspirationImageCommand(appointmentId, uploadToken ?? string.Empty, stream),
      ct);

    return Ok(new UploadInspirationResponse(result.Url, result.ThumbnailUrl, result.Key));
  }
}
