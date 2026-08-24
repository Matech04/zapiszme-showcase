using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.VatRates.Dtos;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.VatRates.Queries.GetVatRates;

public record GetVatRatesQuery : IRequest<List<VatRateDto>>;

internal class GetVatRatesHandler : TenantHandler<GetVatRatesQuery, List<VatRateDto>>
{
  private readonly IApplicationDbContext _context;

  public GetVatRatesHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<VatRateDto>> Handle(GetVatRatesQuery request, CancellationToken ct)
  {
    return await _context.VatRates
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId)
      .Select(x => new VatRateDto(x.Id, x.Name, x.Value, x.IsDefault, x.IsActive))
      .ToListAsync(ct);
  }
}