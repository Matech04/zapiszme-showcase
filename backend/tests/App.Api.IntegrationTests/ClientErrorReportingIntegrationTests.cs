using System.Net;
using System.Net.Http.Json;
using App.Api.Controllers.Booking;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Raportowanie awarii publicznego kalendarza (POST /api/booking-diagnostics/client-error).
///
/// Endpoint istnieje po to, żeby błąd u klientki („Load failed" na telefonie) w ogóle do nas
/// dotarł. Testy pilnują trzech rzeczy, bez których byłby bezużyteczny albo groźny: przechodzi
/// anonimowo i bez tokenu antiforgery, nie da się nim wstrzyknąć fałszywej linii logu, i nie da
/// się nim wpompować megabajtów do Seq.
///
/// Treści samej linii logu celowo NIE sprawdzamy przez host: Serilog trzyma logger statycznie,
/// więc przy równolegle stawianych fabrykach przechwycenie zdarzenia jest niedeterministyczne
/// (test przechodził solo, a padał w pełnym przebiegu). Sanityzację — czyli to, co realnie
/// decyduje o bezpieczeństwie wpisu — sprawdzamy wprost na <see cref="ClientErrorReportSanitizer"/>.
/// </summary>
public sealed class ClientErrorReportingIntegrationTests
{
  private const string Endpoint = "/api/booking-diagnostics/client-error";

  private sealed record ClientErrorPayload(
    string CorrelationId,
    string Operation,
    string Kind,
    string Message,
    int? Status,
    string SalonSlug,
    string PageUrl,
    string? Context);

  private static ClientErrorPayload ValidPayload(string message = "TypeError | Load failed") => new(
    CorrelationId: "3f2b1c9d-1111-2222-3333-444455556666",
    Operation: "loadSalon",
    Kind: "network",
    Message: message,
    Status: null,
    SalonSlug: "salon-testowy",
    PageUrl: "https://zapisz.me/salon-testowy",
    Context: null);

  [Fact]
  public async Task Client_error_report_is_accepted_anonymously_without_antiforgery_token()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(Endpoint, ValidPayload(), ct);

    // 204 bez logowania i bez tokenu CSRF — zgłoszenie leci właśnie wtedy, gdy front jest
    // w rozsypce (czasem przy zamykaniu karty), więc nie może zależeć od stanu sesji.
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task Report_route_is_not_treated_as_a_salon_slug()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(Endpoint, ValidPayload(), ct);

    // Gdyby trasa siedziała pod /api/booking/{slug}/..., TenantIdentifierMiddleware wziąłby drugi
    // segment za slug i odciął zgłoszenie 404-ką — akurat wtedy, gdy jest najbardziej potrzebne.
    Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Oversized_report_is_rejected_by_model_validation()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(Endpoint, ValidPayload(new string('x', 50_000)), ct);

    // [MaxLength] + [ApiController] → 400 zanim cokolwiek trafi do logu. Endpoint nie może być
    // tanim sposobem na zapchanie Seq.
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public void Sanitizer_strips_newlines_so_a_report_cannot_forge_a_second_log_line()
  {
    // Klasyczne log forging: klient próbuje dopisać własną „linię logu".
    var forged = "Load failed\r\n[FATAL] Salon skasowany przez admina";

    var flattened = ClientErrorReportSanitizer.Flatten(forged, 600);

    Assert.NotNull(flattened);
    Assert.DoesNotContain('\n', flattened);
    Assert.DoesNotContain('\r', flattened);
    // Treść zostaje (jest diagnostyczna) — traci tylko zdolność udawania osobnego wpisu.
    Assert.Contains("[FATAL] Salon skasowany przez admina", flattened, StringComparison.Ordinal);
  }

  [Fact]
  public void Sanitizer_truncates_to_the_declared_limit_and_drops_empty_values()
  {
    Assert.Equal(10, ClientErrorReportSanitizer.Flatten(new string('x', 5_000), 10)!.Length);
    Assert.Null(ClientErrorReportSanitizer.Flatten("   ", 100));
    Assert.Null(ClientErrorReportSanitizer.Flatten(null, 100));
  }
}
