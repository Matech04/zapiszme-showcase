using App.Application.Common.Interfaces;
using App.Domain.Aggregates.NotificationAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications;

/// <summary>
/// Liczy udane SMS bieżącego miesiąca dla salonu (jawny <c>tenantId</c> + <c>IgnoreQueryFilters</c>,
/// żeby działać też z tła/przypomnień bez kontekstu tenanta) i porównuje z twardym limitem.
/// </summary>
public sealed class SmsUsageGuard : ISmsUsageGuard
{
  private readonly IApplicationDbContext _context;

  public SmsUsageGuard(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<bool> IsWithinMonthlyCapAsync(Guid tenantId, CancellationToken ct)
  {
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    if (tenant is null)
    {
      return true;
    }

    var cap = tenant.Subscription.EffectiveMonthlySmsCap;

    var now = DateTime.UtcNow;
    var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var used = await _context.NotificationUsage
      .IgnoreQueryFilters()
      .CountAsync(
        u => u.TenantId == tenantId
          && u.Channel == NotificationDeliveryChannel.Sms
          && u.Success
          && u.SentAtUtc >= start && u.SentAtUtc < end,
        ct);

    return used < cap;
  }
}
