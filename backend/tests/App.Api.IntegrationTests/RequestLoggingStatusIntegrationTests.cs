using System.Net;
using App.Api.Authentication;
using App.Api.E2eSupport;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace App.Api.IntegrationTests;

/// <summary>
/// Log żądania musi raportować status, który FAKTYCZNIE poszedł do klienta.
///
/// Powód powstania: `UseExceptionHandler` stało w potoku PRZED `UseSerilogRequestLogging`, czyli było
/// od niego zewnętrzne. Wyjątek domenowy (np. <c>NoTenantHeader</c>) przechodził więc przez middleware
/// Serilloga w górę — ten zapisywał Error, StatusCode 500 i pełny stack trace — a dopiero handler,
/// stojący wyżej, zamieniał go na czyste 400 `tenant.missing`, i TO dostawał klient. W strumieniu
/// produkcyjnym dawało to 14 fałszywych piątek na 2 godziny, każdą ze stack tracem, przy zerowej
/// realnej awarii. Diagnostyka 5xx nie ucierpiała: `GlobalFallbackExceptionHandler` w gałęzi
/// <c>default</c> sam loguje wyjątek ze stackiem.
///
/// Sonda podpina się przez `ReadFrom.Services(services)` w konfiguracji Serilloga (Program.cs) —
/// każdy <see cref="ILogEventSink"/> z DI dostaje zdarzenia tego hosta.
/// </summary>
public sealed class RequestLoggingStatusIntegrationTests
{
  [Fact]
  public async Task Wyjatek_zamieniony_na_400_loguje_sie_jako_400_a_nie_500()
  {
    var sink = new CapturingSink();
    using var factory = new BookingApiApplicationFactory();
    using var configured = factory.WithWebHostBuilder(builder =>
      builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink)));

    RestApiIntegrationSeed.Seed(configured.Services);

    // Admin platformy nie ma powiązanego pracownika, więc TenantIdentifierMiddleware nie rozwiąże
    // tenanta i handler zapytania rzuci NoTenantHeader — dokładnie ta ścieżka, która na produkcji
    // logowała się jako 500.
    var client = configured.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId, IntegrationTestUserIds.SystemAdmin.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Admin");

    var ct = TestContext.Current.CancellationToken;
    var response = await client.GetAsync("/api/Employees", ct);

    // Klient dostawał 400 także PRZED poprawką — to nie tu był błąd.
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    // Sink dostaje zdarzenia WYŁĄCZNIE tego hosta, więc filtr po samej ścieżce jest jednoznaczny —
    // i daje czytelny komunikat przy regresji („oczekiwano 400, było 500") zamiast pustej kolekcji.
    var wpis = Assert.Single(
      sink.Snapshot(),
      e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal)
           && Wartosc(e, "RequestPath") == "/api/Employees");

    // Sedno: log musi nieść status faktycznie zwrócony klientowi, bez poziomu Error i bez stacku.
    Assert.Equal("400", Wartosc(wpis, "StatusCode"));
    Assert.Equal(LogEventLevel.Warning, wpis.Level);
    Assert.Null(wpis.Exception);
  }

  private static string? Wartosc(LogEvent e, string nazwa) =>
    e.Properties.TryGetValue(nazwa, out var value)
      ? value.ToString().Trim('"')
      : null;

  private sealed class CapturingSink : ILogEventSink
  {
    private readonly List<LogEvent> _events = [];

    public void Emit(LogEvent logEvent)
    {
      lock (_events)
      {
        _events.Add(logEvent);
      }
    }

    public IReadOnlyList<LogEvent> Snapshot()
    {
      lock (_events)
      {
        return [.. _events];
      }
    }
  }
}
