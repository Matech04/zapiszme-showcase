using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

/// <summary>
/// Przycinanie i spłaszczanie tekstu ze zgłoszenia klienta. Wydzielone z kontrolera, żeby dało się
/// to sprawdzić testem wprost — bez stawiania hosta i łapania logów (Serilog trzyma logger statycznie,
/// więc przy równoległych testach przechwycenie linii bywa niedeterministyczne).
/// </summary>
public static class ClientErrorReportSanitizer
{
  /// <summary>
  /// Zwraca tekst bez CR/LF/TAB i przycięty do <paramref name="maxLength"/>, albo <c>null</c> dla pustego.
  /// Znaki nowej linii lecą precz, bo w logu pozwalają udawać osobny wpis (log forging).
  /// </summary>
  public static string? Flatten(string? value, int maxLength)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var flattened = value
      .Replace('\r', ' ')
      .Replace('\n', ' ')
      .Replace('\t', ' ')
      .Trim();

    return flattened.Length > maxLength ? flattened[..maxLength] : flattened;
  }
}

/// <summary>Zgłoszenie awarii z przeglądarki klientki (publiczny kalendarz rezerwacji).</summary>
/// <param name="CorrelationId">Identyfikator karty przeglądarki; klientka widzi jego skrót na ekranie błędu.</param>
/// <param name="Operation">Operacja, która padła — np. <c>loadSalon</c>, <c>loadSlots</c>, <c>render</c>.</param>
/// <param name="Kind">Klasyfikacja z frontu: <c>network</c>, <c>server</c>, <c>misconfigured</c>, <c>unknown</c>.</param>
/// <param name="Message">Techniczna treść błędu (nazwa wyjątku + komunikat przeglądarki).</param>
public record ClientErrorReportRequest(
  [MaxLength(64)] string? CorrelationId,
  [MaxLength(100)] string? Operation,
  [MaxLength(32)] string? Kind,
  [MaxLength(600)] string? Message,
  int? Status,
  [MaxLength(120)] string? SalonSlug,
  [MaxLength(400)] string? PageUrl,
  [MaxLength(600)] string? Context);

/// <summary>
/// Odbiornik błędów publicznego kalendarza. Klientka salonu nie zgłosi nam błędu z konsoli
/// przeglądarki — po prostu odejdzie — więc front raportuje tu każdą nierozpoznaną awarię
/// (transport, 5xx, crash renderowania), a Serilog wypycha ją do Seq.
///
/// Trasa CELOWO poza wzorcem <c>api/booking/{slug}/...</c>: <c>TenantIdentifierMiddleware</c>
/// potraktowałby drugi segment jako slug i odciął zgłoszenie 404-ką — a raport o zepsutym
/// kalendarzu jest najbardziej potrzebny właśnie wtedy, gdy ze slugiem coś jest nie tak
/// (ten sam powód, dla którego <see cref="PublicBookingDomainController"/> stoi obok, nie w środku).
///
/// Endpoint jest anonimowy i nic nie zapisuje w bazie: bez rekordów, bez tenanta, wyłącznie log.
/// Chroni go osobny, wąski limit per IP (<c>PublicClientDiagnostics</c>) — dlatego nie zjada
/// budżetu żądań rezerwacji i nie nadaje się na tani generator szumu w logach.
/// </summary>
[AllowAnonymous]
[Route("api/booking-diagnostics")]
[EnableRateLimiting("PublicClientDiagnostics")]
public class PublicClientDiagnosticsController : BookingApiControllerBase
{
  private readonly ILogger<PublicClientDiagnosticsController> _logger;

  public PublicClientDiagnosticsController(ILogger<PublicClientDiagnosticsController> logger)
  {
    _logger = logger;
  }

  [HttpPost("client-error")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public IActionResult ReportClientError([FromBody] ClientErrorReportRequest request)
  {
    // Wszystko poniżej pochodzi od anonimowego klienta, więc nic nie trafia do logu w oryginale:
    // CR/LF pozwoliłby wstrzyknąć fałszywą linię logu, a brak limitu długości — zapchać Seq.
    var kind = ClientErrorReportSanitizer.Flatten(request.Kind, 32);
    var operation = ClientErrorReportSanitizer.Flatten(request.Operation, 100);
    var slug = ClientErrorReportSanitizer.Flatten(request.SalonSlug, 120);
    var correlationId = ClientErrorReportSanitizer.Flatten(request.CorrelationId, 64);
    var message = ClientErrorReportSanitizer.Flatten(request.Message, 600);
    var pageUrl = ClientErrorReportSanitizer.Flatten(request.PageUrl, 400);
    var context = ClientErrorReportSanitizer.Flatten(request.Context, 600);
    var userAgent = ClientErrorReportSanitizer.Flatten(Request.Headers.UserAgent.ToString(), 300);

    // Warning, nie Error: to awaria U KLIENTA (najczęściej jego sieć), a nie wyjątek serwera —
    // Error zarezerwowany jest dla rzeczy, które wybuchły po naszej stronie i mają stack trace.
    _logger.LogWarning(
      "Awaria klienta rezerwacji: {ClientErrorKind} w {ClientErrorOperation} (salon {BookingSlug}, HTTP {ClientErrorStatus}). "
      + "Correlation {ClientCorrelationId}. Treść: {ClientErrorMessage}. Strona: {ClientPageUrl}. "
      + "Kontekst: {ClientErrorContext}. UA: {ClientUserAgent}",
      kind ?? "unknown",
      operation ?? "unknown",
      slug ?? "-",
      request.Status,
      correlationId ?? "-",
      message ?? "-",
      pageUrl ?? "-",
      context ?? "-",
      userAgent ?? "-");

    return NoContent();
  }
}
