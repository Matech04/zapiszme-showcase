using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// DEV-CONFIRM-URL — <c>RegisterOwnerResponse.ConfirmEmailUrl</c>: podgląd linku potwierdzającego
/// wprost w odpowiedzi rejestracji, żeby dało się przeklikać flow bez skrzynki pocztowej.
///
/// SEC: to pole niesie token potwierdzający e-mail. Poza <c>Development</c> jego wystawienie
/// znaczyłoby, że każdy może zarejestrować konto na CUDZY adres i potwierdzić je bez dostępu do
/// skrzynki — potwierdzenie maila przestaje czegokolwiek dowodzić. Bramka jest węższa niż przy
/// logu `[DEV] Confirm-email URL` (tam `!IsProduction()`), bo log zostaje na serwerze, a to leci
/// po sieci do wywołującego.
///
/// Pinujemy tu stronę BEZPIECZEŃSTWA (poza dev = brak linku). Czemu nie ma strony pozytywnej —
/// patrz komentarz na dole klasy.
/// </summary>
public sealed class DevConfirmEmailUrlIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record RegisterProbe(Guid UserId, string Email, bool EmailConfirmed, string? ConfirmEmailUrl);


  // Fabryka stawia env Testing — czyli NIE Development. Link musi być pusty.
  [Fact]
  public async Task Confirm_email_url_is_not_exposed_outside_development()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateClient().PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "owner@dev-url-gate.local",
        password = "Password123!",
        phoneNumber = "+48501777001",
        turnstileToken = (string?)null,
        promoCode = (string?)null,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<RegisterProbe>(JsonRead, ct);
    Assert.NotNull(body);
    Assert.Null(body!.ConfirmEmailUrl);
  }

  // ── Dlaczego nie ma tu testu strony POZYTYWNEJ (że w Development link JEST) ──────────────
  //
  // Bo takiego testu nie da się tu napisać hermetycznie. `WithWebHostBuilder(UseEnvironment(
  // "Development"))` przełącza cały host na profil dev, a InMemory EF jest wybierane WYŁĄCZNIE
  // dla `IsEnvironment("Testing")` (Program.cs) — więc taki test uderza w PRAWDZIWĄ dev-ową bazę
  // z appsettings.Development.json i zostawia w niej konto. Objawia się to nieoczywiście:
  // w izolacji przechodzi (zakłada usera), a w pełnym przebiegu pada na duplikacie e-maila.
  // Sprawdzone i cofnięte 2026-07-16 — nie wracać do tego bez zmiany doboru providera w Program.cs.
  //
  // Ryzyko, które NAPRAWDĘ trzeba pilnować, to wyciek linku poza dev — i to pokrywa test wyżej.
  // Stronę pozytywną weryfikujemy ręcznie (jednorazowo, przy zmianie tej ścieżki):
  //   curl -X POST http://localhost:<port>/api/auth/register-owner … → pole `confirmEmailUrl`.
}
