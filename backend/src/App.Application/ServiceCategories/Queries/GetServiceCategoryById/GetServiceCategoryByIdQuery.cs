using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.ServiceCategories.Dtos;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.ServiceCategories.Queries.GetServiceCategoryById;

public record GetServiceCategoryByIdQuery(Guid Id) : IRequest<ServiceCategoryDto>;

internal class GetServiceCategoryByIdHandler : TenantHandler<GetServiceCategoryByIdQuery, ServiceCategoryDto>
{
  private readonly IApplicationDbContext _context;

  public GetServiceCategoryByIdHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<ServiceCategoryDto> Handle(GetServiceCategoryByIdQuery request, CancellationToken ct)
  {
    var category = await _context.ServiceCategories
      .Where(x => x.TenantId == TenantId && x.Id == request.Id)
      .Select(x => new ServiceCategoryDto(x.Id, x.Name, x.OrderIndex))
      .FirstOrDefaultAsync(ct);

    return category ?? throw new NotFoundException(nameof(ServiceCategory), request.Id);
  }
}