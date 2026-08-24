using App.Application.Appointments;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Tryb minimalizacji danych „nie przechowuj historii wizyt" (<c>Tenant.DoNotRetainAppointmentHistory</c>).
/// Okresowo (domyślnie co 1h) TRWALE usuwa (hard-delete) z bazy wizyty salonów z włączoną flagą, które są
/// jednocześnie w stanie TERMINALNYM (Completed/Canceled) i z datą w PRZESZŁOŚCI względem strefy czasowej
/// salonu (<c>Tenant.TimeZoneId</c>). Owned <c>AppointmentServiceItems</c> kasowane są kaskadowo razem z wizytą.
///
/// Operacja DESTRUKCYJNA — celowo konserwatywna: wizyty bieżące/przyszłe oraz aktywne holdy
/// (Pending/AwaitingOtp/Booked/InProgress) pozostają NIETKNIĘTE; przypomnienia i obsługa działają normalnie.
/// Logujemy tylko LICZBY (żadnego PII). W środowisku Testing nie startujemy hosted service (zob. Program.cs) —
/// logika czyszczenia jest wydzielona do statycznego <see cref="RunCycleAsync(ApplicationDbContext, DateTime, int, CancellationToken, ILogger)"/>.
///
/// Konfiguracja: <c>AppointmentHistory:PurgeIntervalMinutes</c> (default 60),
/// <c>AppointmentHistory:PurgeBatchSize</c> (default 500).
/// </summary>
public sealed class AppointmentHistoryPurgeHostedService : BackgroundService
{
  private const int DefaultBatchSize = 500;

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<AppointmentHistoryPurgeHostedService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _interval;
  private readonly int _batchSize;

  public AppointmentHistoryPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AppointmentHistoryPurgeHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var intervalMinutes = configuration.GetValue<double?>("AppointmentHistory:PurgeIntervalMinutes") ?? 60d;
    var batchSize = configuration.GetValue<int?>("AppointmentHistory:PurgeBatchSize") ?? DefaultBatchSize;
    _interval = TimeSpan.FromMinutes(intervalMinutes);
    _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
  }

  /// <summary>E2E backdoor entry point — wymusza jeden cykl bez czekania na interwał.</summary>
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
        _logger.LogError(ex, "Czyszczenie historii wizyt — błąd cyklu.");
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
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _batchSize, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: jeden cykl czyszczenia. Nie czeka, nie tworzy scope ani logger-a —
  /// wszystko wstrzykiwane z zewnątrz.
  /// </summary>
  internal static async Task RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    int batchSize,
    CancellationToken ct,
    ILogger logger)
  {
    // Tylko salony, które JAWNIE włączyły tryb minimalizacji danych. IgnoreQueryFilters bo job działa
    // bez kontekstu tenanta; iterujemy wprost po liście flagowanych tenantów i ZAWSZE filtrujemy po TenantId.
    var flaggedTenants = await db.Tenants
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(t => t.DoNotRetainAppointmentHistory)
      .Select(t => new { t.Id, t.TimeZoneId })
      .ToListAsync(ct);

    if (flaggedTenants.Count == 0)
    {
      return;
    }

    var totalPurged = 0;
    var tenantsTouched = 0;

    foreach (var tenant in flaggedTenants)
    {
      var timeZoneId = string.IsNullOrWhiteSpace(tenant.TimeZoneId) ? "Europe/Warsaw" : tenant.TimeZoneId;
      // Liczymy „dziś" z PRZEKAZANEGO utcNow (a nie realnego zegara) — job honoruje wstrzyknięty
      // TimeProvider; w produkcji to realny czas, więc zachowanie bez zmian, ale test jest deterministyczny.
      var today = AppointmentScheduleGuard.TodayInBusinessTimeZone(timeZoneId, utcNow);

      var purgedForTenant = 0;

      // Usuwamy wsadowo (batch), żeby nie ładować całej historii dużego salonu naraz.
      // Warunek terminalny + przeszłość liczony w bazie; jawny filtr TenantId chroni izolację.
      while (true)
      {
        ct.ThrowIfCancellationRequested();

        var batch = await db.Appointments
          .IgnoreQueryFilters()
          .AsTracking()
          .Where(a => a.TenantId == tenant.Id
            && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Canceled)
            && a.Date < today)
          .OrderBy(a => a.Date)
          .Take(batchSize)
          .ToListAsync(ct);

        if (batch.Count == 0)
        {
          break;
        }

        // Defensywnie — predykat domenowy potwierdza regułę usuwania (terminalne + przeszłość).
        var toRemove = batch
          .Where(a => AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(a.Status, a.Date, today))
          .ToList();

        if (toRemove.Count == 0)
        {
          break;
        }

        db.Appointments.RemoveRange(toRemove);
        await db.SaveChangesAsync(ct);

        purgedForTenant += toRemove.Count;

        // Wczytany batch był pełny → mogą być kolejne. Niepełny → koniec dla tego tenanta.
        if (batch.Count < batchSize)
        {
          break;
        }
      }

      if (purgedForTenant > 0)
      {
        totalPurged += purgedForTenant;
        tenantsTouched++;
      }
    }

    if (totalPurged > 0)
    {
      logger.LogInformation(
        "Czyszczenie historii wizyt: trwale usunięto {PurgedCount} wizyt w {TenantCount} salonach (utcNow={UtcNow:o}).",
        totalPurged,
        tenantsTouched,
        utcNow);
    }
  }
}
