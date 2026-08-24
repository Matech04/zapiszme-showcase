using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo hard-kasuje efemeryczne demo-tenanty (<c>Tenant.IsDemo</c>) starsze niż TTL.
/// Razem z tenantem znika cały jego graf danych (wizyty, klientki, pracownik-właściciel,
/// usługi, powiadomienia) oraz powiązane konto Identity — żeby tryb demo nie zostawiał
/// sierot ani nie zajmował slugów. To świadomy wyjątek od soft-delete: dane demo są
/// jednorazowe i mają być fizycznie usuwane.
///
/// TTL: <c>Demo:TtlHours</c> (default 24h). Interwał: <c>Demo:CleanupIntervalMinutes</c>
/// (default 30). W środowiskach Testing / E2E nie startujemy hosted service (zob. Program.cs).
/// </summary>
public sealed class DemoTenantCleanupHostedService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<DemoTenantCleanupHostedService> _logger;
  private readonly TimeSpan _ttl;
  private readonly TimeSpan _interval;
  private readonly TimeProvider _timeProvider;

  public DemoTenantCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DemoTenantCleanupHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var ttlHours = configuration.GetValue<double?>("Demo:TtlHours") ?? 24d;
    var intervalMinutes = configuration.GetValue<double?>("Demo:CleanupIntervalMinutes") ?? 30d;
    _ttl = TimeSpan.FromHours(ttlHours);
    _interval = TimeSpan.FromMinutes(intervalMinutes);
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
        _logger.LogError(ex, "Cleanup demo-tenantów — błąd cyklu.");
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
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _ttl, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: nie czeka, nie tworzy własnego scope ani logger-a — wszystko
  /// wstrzykiwane z zewnątrz. Wykonuje JEDEN cykl czyszczenia.
  /// </summary>
  internal static async Task RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    TimeSpan ttl,
    CancellationToken ct,
    ILogger logger)
  {
    var cutoff = utcNow - ttl;

    var expiredTenantIds = await db.Tenants
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(t => t.IsDemo && t.DemoCreatedAtUtc != null && t.DemoCreatedAtUtc < cutoff)
      .Select(t => t.Id)
      .ToListAsync(ct);

    if (expiredTenantIds.Count == 0)
    {
      return;
    }

    var deletedTenants = 0;

    foreach (var tenantId in expiredTenantIds)
    {
      try
      {
        await DeleteDemoTenantAsync(db, tenantId, ct);
        deletedTenants++;
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Cleanup demo-tenanta {TenantId} nie powiódł się — pomijam i lecę dalej.",
          tenantId);
      }
    }

    if (deletedTenants > 0)
    {
      logger.LogInformation(
        "Cleanup demo-tenantów: usunięto {DeletedTenants} tenantów (cutoff={Cutoff:o}).",
        deletedTenants,
        cutoff);
    }
  }

  // Pełny hard-delete grafu demo-tenanta deleguje do współdzielonego purgera (jedno źródło
  // prawdy z DeleteTenantCommand w panelu admina — patrz TenantPurgeService).
  private static Task DeleteDemoTenantAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    => TenantPurgeService.PurgeCoreAsync(db, tenantId, ct);
}
