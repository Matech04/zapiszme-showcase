using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Endpointy postępu przewodników (<c>/api/guides</c>) — kontrakt, na którym stoi katalog
/// <c>/admin/guides</c> w dashboardzie.
///
/// Sedno: stan jest per UŻYTKOWNIK (claim), nie per salon, więc encja nie ma filtra tenanta.
/// Te testy pilnują, że jedyną realną barierą — filtrem po UserId — nie da się przejść oraz że
/// zapis jest idempotentny (front woła go bez sprawdzania stanu).
///
/// Wiersze w <c>AspNetUsers</c> dla <c>IntegrationTestUserIds</c> zakłada
/// <c>RestApiIntegrationSeed</c>, więc klucz obcy do użytkownika ma się o co oprzeć.
/// </summary>
public sealed class GuideCompletionsIntegrationTests
{
  private const string GuideId = "set-weekly-schedule";

  [Fact]
  public async Task Nowy_uzytkownik_nie_ma_ukonczonych_przewodnikow()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/guides/completions", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var completions = await response.Content.ReadFromJsonAsync<List<string>>(ct);
    Assert.NotNull(completions);
    Assert.Empty(completions!);
  }

  [Fact]
  public async Task Pelny_cykl_oznacz_odczytaj_zresetuj()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var marked = await client.PostAsync($"/api/guides/{GuideId}/complete", null, ct);
    Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);

    var afterMark = await client.GetFromJsonAsync<List<string>>("/api/guides/completions", ct);
    Assert.Equal(new[] { GuideId }, afterMark);

    var reset = await client.DeleteAsync($"/api/guides/{GuideId}/complete", ct);
    Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

    var afterReset = await client.GetFromJsonAsync<List<string>>("/api/guides/completions", ct);
    Assert.Empty(afterReset!);
  }

  [Fact]
  public async Task Powtorne_oznaczenie_nie_duplikuje_wpisu()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsync($"/api/guides/{GuideId}/complete", null, ct);
    var second = await client.PostAsync($"/api/guides/{GuideId}/complete", null, ct);

    // Idempotencja jest kontacktem, nie przypadkiem: katalog odtwarza przewodnik bez sprawdzania stanu.
    Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

    var completions = await client.GetFromJsonAsync<List<string>>("/api/guides/completions", ct);
    Assert.Equal(new[] { GuideId }, completions);
  }

  [Fact]
  public async Task Postep_nie_przecieka_miedzy_uzytkownikami()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Owner i admin systemowy to dwa różne konta Identity — admin nie ma nawet tenanta,
    // więc ten test pokrywa też ścieżkę „użytkownik bez salonu".
    await factory.CreateOwnerClient().PostAsync($"/api/guides/{GuideId}/complete", null, ct);

    var adminCompletions = await factory.CreateAdminClient()
      .GetFromJsonAsync<List<string>>("/api/guides/completions", ct);

    Assert.Empty(adminCompletions!);
  }

  [Fact]
  public async Task Anonim_nie_ma_dostepu()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/guides/completions", ct);
    Assert.True(
      response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
      $"Oczekiwano odmowy dostępu, było {response.StatusCode}");
  }

  [Fact]
  public async Task Identyfikator_spoza_kebab_case_jest_odrzucany()
  {
    using var factory = new BookingApiApplicationFactory();
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync("/api/guides/NIE_kebab_case/complete", null, ct);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
