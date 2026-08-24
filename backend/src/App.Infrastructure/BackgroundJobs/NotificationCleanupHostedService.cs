using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo usuwa stare powiadomienia in-app (<c>Notifications</c>) — niezależnie od stanu
/// „przeczytane". Dzwonek w panelu pokazuje tylko ostatnie 50 pozycji (zob.
/// <c>GetNotificationsQuery</c>), więc rekordy starsze niż okno retencji są martwym balastem,
/// a bez tego joba tabela rosłaby w nieskończoność (1 wiersz na każde zdarzenie powiadomienia).
///
/// Retencja: <c>Notification:CleanupRetentionDays</c> (default 14). Interwał:
/// <c>Notification:CleanupIntervalHours</c> (default 24). W Testing/E2E nie startuje jako pętla
/// (zob. Program.cs) — w E2E dostępny przez backdoor.
/// </summary>
public sealed class NotificationCleanupHostedService : BackgroundService
{
  private const int BatchSize = 5000;

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<NotificationCleanupHostedService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _retention;
  private readonly TimeSpan _interval;

  public NotificationCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationCleanupHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var retentionDays = configuration.GetValue<double?>("Notification:CleanupRetentionDays") ?? 14d;
    var intervalHours = configuration.GetValue<double?>("Notification:CleanupIntervalHours") ?? 24d;
    _retention = TimeSpan.FromDays(retentionDays);
    _interval = TimeSpan.FromHours(intervalHours);
  }

  /// <summary>E2E / test backdoor — wymusza jeden cykl bez czekania na interwał.</summary>
  internal Task ExecuteOnceAsync(CancellationToken ct) => RunCycleAsync(ct);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunCycleAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Cleanup powiadomień in-app — błąd cyklu.");
      }

      try
      {
        await Task.Delay(_interval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  internal async Task RunCycleAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _retention, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: jeden cykl. <c>IgnoreQueryFilters</c> — job leci bez kontekstu
  /// tenanta, więc bez tego globalny filtr <c>TenantId == null</c> nie złapałby niczego.
  /// <c>RemoveRange</c> zamiast <c>ExecuteDelete</c> — działa na każdym providerze (także InMemory
  /// w testach); wolumen w jednym oknie jest ograniczony, a duże zaległości schodzą batchami w
  /// kolejnych cyklach.
  /// </summary>
  internal static async Task<int> RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    TimeSpan retention,
    CancellationToken ct,
    ILogger logger)
  {
    var cutoff = utcNow - retention;

    // OrderBy przed Take: bez niego baza zwraca DOWOLNĄ paczkę pasujących wierszy, a EF sygnalizuje
    // to ostrzeżeniem „row limiting operator without OrderBy". Najstarsze najpierw to właściwa
    // kolejność dla retencji — zaległości drenujemy od końca, a kolejne cykle nie przeskakują
    // po zbiorze losowo.
    var stale = await db.Notifications
      .IgnoreQueryFilters()
      .Where(n => n.CreatedAtUtc < cutoff)
      .OrderBy(n => n.CreatedAtUtc)
      .Take(BatchSize)
      .ToListAsync(ct);

    if (stale.Count == 0)
    {
      return 0;
    }

    db.Notifications.RemoveRange(stale);
    await db.SaveChangesAsync(ct);

    logger.LogInformation(
      "Cleanup powiadomień in-app: usunięto {Count} (cutoff={Cutoff:o}).", stale.Count, cutoff);

    return stale.Count;
  }
}
