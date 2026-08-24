using System.Text.Json;
using App.Api.Middleware;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Api.IntegrationTests;

/// <summary>
/// Test jednostkowy handlera, nie ścieżki HTTP — middleware antiforgery jest celowo wyłączone
/// w środowisku Testing (patrz Program.cs), więc realnego 400 nie da się wywołać przez klienta.
/// </summary>
public class GlobalFallbackExceptionHandlerTests
{
  private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
  {
    var handler = new GlobalFallbackExceptionHandler(NullLogger<GlobalFallbackExceptionHandler>.Instance);

    var context = new DefaultHttpContext();
    context.Request.Path = "/api/Appointments";
    var body = new MemoryStream();
    context.Response.Body = body;

    await handler.TryHandleAsync(context, exception, CancellationToken.None);

    body.Position = 0;
    using var document = await JsonDocument.ParseAsync(body);
    return (context.Response.StatusCode, document.RootElement.Clone());
  }

  /// <summary>
  /// Wygasły token antiforgery wpadał w `default` i wracał jako 500 „Krytyczny błąd serwera":
  /// użytkownik widział komunikat o awarii zamiast prośby o odświeżenie, a log alarmował
  /// o błędzie serwera przy zwykłym wygaśnięciu tokenu.
  /// </summary>
  [Fact]
  public async Task Antiforgery_failure_is_400_not_500()
  {
    var (status, body) = await HandleAsync(new AntiforgeryValidationException("token nieobecny"));

    Assert.Equal(StatusCodes.Status400BadRequest, status);
    Assert.Equal(ErrorCodes.AntiforgeryInvalid, body.GetProperty("errorCode").GetString());
  }

  /// <summary>
  /// Kluczowe rozróżnienie: front wylogowuje użytkownika przy KAŻDYM 401 z żądania domenowego
  /// (errorInterceptor → SessionExpiryService). Gdyby antiforgery mapowało się na 401, wygaśnięcie
  /// tokenu wyrzucałoby z panelu mimo w pełni ważnej sesji.
  /// </summary>
  [Fact]
  public async Task Antiforgery_failure_is_never_401()
  {
    var (status, _) = await HandleAsync(new AntiforgeryValidationException("token nieobecny"));

    Assert.NotEqual(StatusCodes.Status401Unauthorized, status);
  }

  /// <summary>Nieznany wyjątek nadal ma być 500 — poprawka nie może rozmyć sygnału o awarii.</summary>
  [Fact]
  public async Task Unknown_exception_stays_500()
  {
    var (status, body) = await HandleAsync(new InvalidTimeZoneException("coś nieprzewidzianego"));

    Assert.Equal(StatusCodes.Status500InternalServerError, status);
    Assert.Equal(ErrorCodes.InternalError, body.GetProperty("errorCode").GetString());
  }
}
