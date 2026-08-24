using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo usuwa wygasłe skrócone linki (<c>ShortLinks</c>) starsze niż okno karencji. Linki to
/// tylko mapowanie kod→URL operatora (np. Stripe Checkout) — po wygaśnięciu są bezużyteczne, a
/// rekord płatności żyje na <c>Appointment</c>, więc nie tracimy nic biznesowego. Bez tego joba
/// tabela rosłaby w nieskończoność (1 wiersz na każdy wygenerowany / zregenerowany link).
///
/// Karencja: <c>ShortLink:CleanupGraceDays</c> (default 7). Interwał: <c>ShortLink:CleanupIntervalHours</c>
/// (default 24). W Testing/E2E nie startuje jako pętla (zob. Program.cs).
/// </summary>
public sealed class ShortLinkCleanupHostedService : BackgroundService
{
  private const int BatchSize = 5000;

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<ShortLinkCleanupHostedService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _grace;
  private readonly TimeSpan _interval;

  public ShortLinkCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ShortLinkCleanupHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var graceDays = configuration.GetValue<double?>("ShortLink:CleanupGraceDays") ?? 7d;
    var intervalHours = configuration.GetValue<double?>("ShortLink:CleanupIntervalHours") ?? 24d;
    _grace = TimeSpan.FromDays(graceDays);
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
        _logger.LogError(ex, "Cleanup skróconych linków — błąd cyklu.");
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
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _grace, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: jeden cykl. <c>RemoveRange</c> zamiast <c>ExecuteDelete</c> — działa
  /// na każdym providerze (także InMemory w testach), a wolumen linków jest niski.
  /// </summary>
  internal static async Task<int> RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    TimeSpan grace,
    CancellationToken ct,
    ILogger logger)
  {
    var cutoff = utcNow - grace;

    // OrderBy przed Take: bez niego baza zwraca DOWOLNĄ paczkę pasujących wierszy (EF sygnalizuje
    // to ostrzeżeniem „row limiting operator without OrderBy"). Najstarsze wygaśnięcia pierwsze,
    // żeby kolejne cykle drenowały zaległość, a nie skakały po zbiorze.
    var expired = await db.ShortLinks
      .Where(l => l.ExpiresAtUtc != null && l.ExpiresAtUtc < cutoff)
      .OrderBy(l => l.ExpiresAtUtc)
      .Take(BatchSize)
      .ToListAsync(ct);

    if (expired.Count == 0)
    {
      return 0;
    }

    db.ShortLinks.RemoveRange(expired);
    await db.SaveChangesAsync(ct);

    logger.LogInformation(
      "Cleanup skróconych linków: usunięto {Count} (cutoff={Cutoff:o}).", expired.Count, cutoff);

    return expired.Count;
  }
}
