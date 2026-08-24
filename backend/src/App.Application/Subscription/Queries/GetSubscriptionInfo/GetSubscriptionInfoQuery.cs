using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Subscription.Dtos;
using App.Application.Subscription.PromoPricing;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Subscription.Queries.GetSubscriptionInfo;

public record GetSubscriptionInfoQuery : IRequest<SubscriptionInfoDto>;

internal class GetSubscriptionInfoQueryHandler : TenantHandler<GetSubscriptionInfoQuery, SubscriptionInfoDto>
{
  private readonly IApplicationDbContext _context;

  public GetSubscriptionInfoQueryHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<SubscriptionInfoDto> Handle(GetSubscriptionInfoQuery request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var sub = tenant.Subscription;

    PromoCodeRedemption? redemption = null;
    PromoCode? promoCode = null;
    if (sub.ActivePromoCodeRedemptionId is { } redemptionId)
    {
      redemption = await _context.PromoCodeRedemptions
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == redemptionId, ct);
      if (redemption is not null)
      {
        promoCode = await _context.PromoCodes
          .AsNoTracking()
          .FirstOrDefaultAsync(p => p.Id == redemption.PromoCodeId, ct);
      }
    }

    var basePrice = PromoPriceCalculator.CalculateBasePrice(sub);
    var finalPrice = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    ActivePromoCodeDto? activePromo = null;
    if (redemption is not null && promoCode is not null && redemption.IsActive)
    {
      activePromo = new ActivePromoCodeDto(
        promoCode.Code,
        promoCode.DiscountType.ToString(),
        promoCode.DiscountValue,
        redemption.ExpiresAt is null ? null : new DateTimeOffset(redemption.ExpiresAt.Value, TimeSpan.Zero));
    }

    // TODO(billing): podpiąć rzeczywisty counter SMS po integracji.
    const int monthlySmsUsed = 0;

    return new SubscriptionInfoDto(
      Status: sub.Status.ToString(),
      EffectiveStatus: sub.EffectiveStatus.ToString(),
      Seats: sub.Seats,
      IsFoundingMember: sub.IsFoundingMember,
      TrialEndsAt: sub.TrialEndsAt,
      CurrentPeriodEndsAt: sub.CurrentPeriodEndsAt,
      IsTrialActive: sub.IsTrialActive,
      DaysRemainingInTrial: sub.DaysRemainingInTrial,
      BaseMonthlyPriceInGrosze: basePrice,
      MonthlyPriceInGrosze: finalPrice,
      MonthlySmsAllowance: sub.MonthlySmsAllowance,
      MonthlySmsUsed: monthlySmsUsed,
      ActivePromoCode: activePromo
    );
  }
}
