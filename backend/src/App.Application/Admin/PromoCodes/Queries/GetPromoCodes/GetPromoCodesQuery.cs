using App.Application.Admin.PromoCodes.Dtos;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PromoCodes.Queries.GetPromoCodes;

public record GetPromoCodesQuery(bool? IsActive = null) : IRequest<List<PromoCodeDto>>;

public class GetPromoCodesQueryHandler : IRequestHandler<GetPromoCodesQuery, List<PromoCodeDto>>
{
  private readonly IApplicationDbContext _context;

  public GetPromoCodesQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<List<PromoCodeDto>> Handle(GetPromoCodesQuery r, CancellationToken ct)
  {
    var q = _context.PromoCodes.AsNoTracking();
    if (r.IsActive is bool active) q = q.Where(p => p.IsActive == active);

    return await q
      .OrderByDescending(p => p.CreatedAt)
      .Select(p => new PromoCodeDto(
        p.Id, p.Code, p.Kind, p.DiscountType, p.DiscountValue,
        p.DurationMonths, p.MaxTotalUses, p.MaxUsesPerTenant, p.CurrentUses,
        p.ValidFrom, p.ValidUntil, p.AppliesTo, p.IsActive, p.Metadata, p.CreatedAt))
      .ToListAsync(ct);
  }
}
