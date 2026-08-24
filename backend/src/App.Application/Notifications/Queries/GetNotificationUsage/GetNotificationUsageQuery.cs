using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Queries.GetNotificationUsage;

/// <summary>
/// Zużycie kanałów wychodzących (SMS / e-mail) salonu w danym miesiącu kalendarzowym (UTC).
/// Gdy <paramref name="Year"/>/<paramref name="Month"/> nie podano — bieżący miesiąc.
/// </summary>
public record GetNotificationUsageQuery(int? Year = null, int? Month = null)
  : IRequest<NotificationUsageDto>;

/// <summary>Rozbicie zużycia na pojedynczy typ powiadomienia w obrębie kanału.</summary>
public record NotificationUsageByTypeDto(
  int Channel,
  int Type,
  int SentCount,
  int FailedCount,
  decimal Points);

public record NotificationUsageDto(
  int Year,
  int Month,
  // Liczba udanych/nieudanych wiadomości SMS (wierszy).
  int SmsSent,
  int SmsFailed,
  // Punkty smsapi.pl — realnie naliczone segmenty (polskie znaki → 70 zn./segment).
  decimal SmsPoints,
  // Zużycie liczone względem limitu = max(liczba wiadomości, ceil(punkty)). Każda wiadomość ≥ 1 kredyt,
  // a wielosegmentowe liczą realny koszt — odporne też na provider zwracający 0 punktów.
  int SmsCreditsUsed,
  int EmailSent,
  int EmailFailed,
  int SmsAllowance,
  int SmsRemaining,
  int SmsOverageCount,
  int OverageUnitPriceGrosze,
  int OverageCostGrosze,
  IReadOnlyList<NotificationUsageByTypeDto> ByType);

internal class GetNotificationUsageQueryHandler
  : TenantHandler<GetNotificationUsageQuery, NotificationUsageDto>
{
  private readonly IApplicationDbContext _context;

  public GetNotificationUsageQueryHandler(
    IApplicationDbContext context,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<NotificationUsageDto> Handle(
    GetNotificationUsageQuery request,
    CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var year = request.Year ?? now.Year;
    var month = request.Month ?? now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    // Agregacja po stronie bazy — grupowanie po kanale/typie/sukcesie. Query filter scope'uje do tenanta.
    var grouped = await _context.NotificationUsage
      .AsNoTracking()
      .Where(u => u.SentAtUtc >= start && u.SentAtUtc < end)
      .GroupBy(u => new { u.Channel, u.Type, u.Success })
      .Select(g => new
      {
        g.Key.Channel,
        g.Key.Type,
        g.Key.Success,
        Count = g.Count(),
        Points = g.Sum(x => x.Points),
      })
      .ToListAsync(ct);

    var smsSent = grouped.Where(g => g.Channel == NotificationDeliveryChannel.Sms && g.Success).Sum(g => g.Count);
    var smsFailed = grouped.Where(g => g.Channel == NotificationDeliveryChannel.Sms && !g.Success).Sum(g => g.Count);
    var smsPoints = grouped.Where(g => g.Channel == NotificationDeliveryChannel.Sms).Sum(g => g.Points);
    var emailSent = grouped.Where(g => g.Channel == NotificationDeliveryChannel.Email && g.Success).Sum(g => g.Count);
    var emailFailed = grouped.Where(g => g.Channel == NotificationDeliveryChannel.Email && !g.Success).Sum(g => g.Count);

    // Limit SMS z subskrypcji (computed z liczby stanowisk). Tenants nie ma query filtra — lookup po Id.
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == TenantId, ct)
      ?? throw new NotFoundException("Tenant", TenantId);

    var allowance = tenant.Subscription.MonthlySmsAllowance;
    // Każdy udany SMS to ≥ 1 kredyt; wielosegmentowy kosztuje tyle, ile punktów smsapi.pl.
    var creditsUsed = Math.Max(smsSent, (int)Math.Ceiling(smsPoints));
    var overage = Math.Max(0, creditsUsed - allowance);

    var byType = grouped
      .GroupBy(g => new { g.Channel, g.Type })
      .Select(g => new NotificationUsageByTypeDto(
        (int)g.Key.Channel,
        (int)g.Key.Type,
        g.Where(x => x.Success).Sum(x => x.Count),
        g.Where(x => !x.Success).Sum(x => x.Count),
        g.Sum(x => x.Points)))
      .OrderBy(t => t.Channel)
      .ThenBy(t => t.Type)
      .ToList();

    return new NotificationUsageDto(
      Year: year,
      Month: month,
      SmsSent: smsSent,
      SmsFailed: smsFailed,
      SmsPoints: smsPoints,
      SmsCreditsUsed: creditsUsed,
      EmailSent: emailSent,
      EmailFailed: emailFailed,
      SmsAllowance: allowance,
      SmsRemaining: Math.Max(0, allowance - creditsUsed),
      SmsOverageCount: overage,
      OverageUnitPriceGrosze: App.Domain.Aggregates.TenantAggregate.Subscription.OverageSmsPriceGrosze,
      OverageCostGrosze: overage * App.Domain.Aggregates.TenantAggregate.Subscription.OverageSmsPriceGrosze,
      ByType: byType);
  }
}
