using App.Application.Admin.PromoCodes.Dtos;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PromoCodes.Queries.GetPromoCodeRedemptions;

public record GetPromoCodeRedemptionsQuery(Guid PromoCodeId) : IRequest<List<PromoCodeRedemptionDto>>;

public class GetPromoCodeRedemptionsQueryHandler : IRequestHandler<GetPromoCodeRedemptionsQuery, List<PromoCodeRedemptionDto>>
{
  private readonly IApplicationDbContext _context;

  public GetPromoCodeRedemptionsQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<List<PromoCodeRedemptionDto>> Handle(GetPromoCodeRedemptionsQuery r, CancellationToken ct)
  {
    var dbContext = (DbContext)_context;
    return await dbContext.Set<App.Domain.Aggregates.TenantAggregate.PromoCodeRedemption>()
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(rd => rd.PromoCodeId == r.PromoCodeId)
      .OrderByDescending(rd => rd.RedeemedAt)
      .Select(rd => new PromoCodeRedemptionDto(
        rd.Id, rd.TenantId, rd.PromoCodeId, rd.RedeemedAt, rd.ExpiresAt,
        rd.DiscountSnapshot, rd.IsActive))
      .ToListAsync(ct);
  }
}
