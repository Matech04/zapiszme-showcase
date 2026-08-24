using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Zapisuje wpis do tabeli <c>NotificationUsage</c> po każdej wysyłce SMS/e-mail. Best-effort:
/// własny <c>SaveChanges</c> (jak <see cref="InAppNotificationChannel"/>) i pełna izolacja błędów —
/// nieudany zapis statystyki tylko loguje ostrzeżenie, nie przerywa powiadomienia.
/// </summary>
public sealed class NotificationUsageRecorder : INotificationUsageRecorder
{
  private readonly IApplicationDbContext _context;
  private readonly ILogger<NotificationUsageRecorder> _logger;

  public NotificationUsageRecorder(IApplicationDbContext context, ILogger<NotificationUsageRecorder> logger)
  {
    _context = context;
    _logger = logger;
  }

  public Task RecordSmsAsync(Guid tenantId, NotificationType type, bool success, decimal points, CancellationToken ct) =>
    SaveAsync(NotificationUsageRecord.ForSms(tenantId, type, success, points, DateTime.UtcNow), ct);

  public Task RecordEmailAsync(Guid tenantId, NotificationType type, bool success, CancellationToken ct) =>
    SaveAsync(NotificationUsageRecord.ForEmail(tenantId, type, success, DateTime.UtcNow), ct);

  private async Task SaveAsync(NotificationUsageRecord record, CancellationToken ct)
  {
    try
    {
      _context.NotificationUsage.Add(record);
      await _context.SaveChangesAsync(ct);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "Nie udało się zapisać zużycia {Channel} dla {Type} (tenant {TenantId}).",
        record.Channel, record.Type, record.TenantId);
    }
  }
}
