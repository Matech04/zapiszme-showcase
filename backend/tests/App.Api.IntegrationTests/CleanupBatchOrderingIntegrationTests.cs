using App.Api.E2eSupport;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace App.Api.IntegrationTests;

/// <summary>
/// Paczkowe zapytania zadań sprzątających muszą mieć `OrderBy` przed `Take`.
///
/// Powód powstania: pięć takich zapytań (powiadomienia, trzy rodzaje OTP, krótkie linki) brało
/// `Take(BatchSize)` bez żadnego sortowania. Baza zwraca wtedy DOWOLNY podzbiór pasujących wierszy,
/// więc kolejne cykle skaczą po zbiorze zamiast go drenować od najstarszych. EF sygnalizuje to
/// ostrzeżeniem `RowLimitingOperationWithoutOrderByWarning` — na produkcji było ich dokładnie pięć,
/// po jednym na każde z tych zapytań (ostrzeżenie leci raz na KSZTAŁT zapytania, przy kompilacji).
///
/// Test nie sprawdza samej kolejności wierszy — przy `BatchSize = 5000` zaseedowanie zbioru
/// większego niż paczka byłoby absurdalnie drogie. Sprawdza to, co realnie regresuje: czy EF nadal
/// uważa któreś z tych zapytań za niedeterministyczne. Sonda wpina się przez
/// `ReadFrom.Services(services)` w konfiguracji Serilloga.
///
/// OGRANICZENIE: wartość ma WYŁĄCZNIE na Postgresie (`INTEGRATION_DB_PROVIDER=Postgres`).
/// Sprawdzone empirycznie: po usunięciu `OrderBy` z zapytania krótkich linków test przechodził
/// na InMemory i wywracał się na Postgresie — potok zapytań InMemory tego ostrzeżenia nie zgłasza.
/// W domyślnym przebiegu ten test jest więc tylko dymem, nie strażnikiem. Zob.
/// DatabaseProviderGuardTests.
/// </summary>
public sealed class CleanupBatchOrderingIntegrationTests
{
  private static readonly DateTime UtcNow = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
  private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

  [Fact]
  public async Task Cykle_sprzatania_nie_zglaszaja_paczek_bez_sortowania()
  {
    var sink = new CapturingSink();
    using var factory = new BookingApiApplicationFactory();
    using var configured = factory.WithWebHostBuilder(builder =>
      builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink)));

    RestApiIntegrationSeed.Seed(configured.Services);
    var ct = TestContext.Current.CancellationToken;

    using (var scope = configured.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // Same cykle wystarczą: ostrzeżenie EF pada przy KOMPILACJI kształtu zapytania, więc leci
      // także wtedy, gdy paczka wyjdzie pusta. Nie musimy niczego kasować, żeby je sprowokować.
      await NotificationCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);
      await OtpCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);
      await ShortLinkCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);
    }

    var bezSortowania = sink.Snapshot()
      .Where(e => e.MessageTemplate.Text.Contains("row limiting operator", StringComparison.OrdinalIgnoreCase))
      .Select(e => e.RenderMessage())
      .ToList();

    Assert.True(
      bezSortowania.Count == 0,
      "Któreś zapytanie paczkowe sprzątania straciło OrderBy przed Take:\n"
        + string.Join("\n", bezSortowania));
  }

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
