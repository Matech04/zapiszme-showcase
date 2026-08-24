using App.Application.Appointments;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Jednorazowe czyszczenie istniejącej historii wizyt na żądanie ownera (zob. <see cref="IAppointmentHistoryPurger"/>).
/// Reguła usuwania (terminalne + przeszłość wg strefy salonu) jest IDENTYCZNA z jobem
/// <c>AppointmentHistoryPurgeHostedService</c> i współdzieli predykat domenowy
/// <see cref="AppointmentCleanupService.ShouldPurgeForNoHistoryRetention"/> — różnica jest taka, że
/// tu działamy dla JEDNEGO, jawnie wskazanego tenanta i NIE wymagamy włączonej flagi
/// (owner świadomie inicjuje akcję z panelu). Usuwamy wsadowo; logujemy tylko LICZBY (żadnego PII).
/// </summary>
public sealed class AppointmentHistoryPurger : IAppointmentHistoryPurger
{
  private const int DefaultBatchSize = 500;

  private readonly ApplicationDbContext _db;
  private readonly int _batchSize;
  private readonly ILogger<AppointmentHistoryPurger> _logger;

  public AppointmentHistoryPurger(
    ApplicationDbContext db,
    IConfiguration configuration,
    ILogger<AppointmentHistoryPurger> logger)
  {
    _db = db;
    _logger = logger;
    var batchSize = configuration.GetValue<int?>("AppointmentHistory:PurgeBatchSize") ?? DefaultBatchSize;
    _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
  }

  public async Task<int> PurgePastHistoryAsync(Guid tenantId, CancellationToken ct = default)
  {
    // Strefa salonu wyznacza „dziś" (jak w jobie). IgnoreQueryFilters + JAWNY filtr TenantId —
    // izolacja tenanta nie zależy od ambient-scope; usuwamy WYŁĄCZNIE dane wskazanego salonu.
    var timeZoneId = await _db.Tenants
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(t => t.Id == tenantId)
      .Select(t => t.TimeZoneId)
      .FirstOrDefaultAsync(ct);

    var today = AppointmentScheduleGuard.TodayInBusinessTimeZone(
      string.IsNullOrWhiteSpace(timeZoneId) ? "Europe/Warsaw" : timeZoneId);

    var purged = 0;

    while (true)
    {
      ct.ThrowIfCancellationRequested();

      var batch = await _db.Appointments
        .IgnoreQueryFilters()
        .AsTracking()
        .Where(a => a.TenantId == tenantId
          && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Canceled)
          && a.Date < today)
        .OrderBy(a => a.Date)
        .Take(_batchSize)
        .ToListAsync(ct);

      if (batch.Count == 0)
      {
        break;
      }

      // Defensywnie — ten sam predykat domenowy co w jobie potwierdza warunek usuwania.
      var toRemove = batch
        .Where(a => AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(a.Status, a.Date, today))
        .ToList();

      if (toRemove.Count == 0)
      {
        break;
      }

      _db.Appointments.RemoveRange(toRemove);
      await _db.SaveChangesAsync(ct);
      purged += toRemove.Count;

      // Pełny batch → mogą być kolejne; niepełny → koniec.
      if (batch.Count < _batchSize)
      {
        break;
      }
    }

    if (purged > 0)
    {
      _logger.LogInformation(
        "Czyszczenie historii na żądanie: trwale usunięto {PurgedCount} wizyt salonu {TenantId}.",
        purged,
        tenantId);
    }

    return purged;
  }
}
