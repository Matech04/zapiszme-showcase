using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.VatRates.Dtos;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.VatRates.Queries.GetVatRateById;

public record GetVatRateByIdQuery(Guid Id) : IRequest<VatRateDto>;

internal class GetVatRateByIdHandler : TenantHandler<GetVatRateByIdQuery, VatRateDto>
{
  private readonly IApplicationDbContext _context;

  public GetVatRateByIdHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<VatRateDto> Handle(GetVatRateByIdQuery request, CancellationToken ct)
  {
    var vatRate = await _context.VatRates
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId && x.Id == request.Id)
      .Select(x => new VatRateDto(x.Id, x.Name, x.Value, x.IsDefault, x.IsActive))
      .FirstOrDefaultAsync(ct);

    return vatRate ?? throw new NotFoundException(nameof(VatRate), request.Id);
  }
}