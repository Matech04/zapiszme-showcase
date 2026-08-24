using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.ServiceCategories.Dtos;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.ServiceCategories.Queries.GetServiceCategories;

public record GetServiceCategoriesQuery : IRequest<List<ServiceCategoryDto>>;

internal class GetServiceCategoriesHandler : TenantHandler<GetServiceCategoriesQuery, List<ServiceCategoryDto>>
{
  private readonly IApplicationDbContext _context;

  public GetServiceCategoriesHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<ServiceCategoryDto>> Handle(GetServiceCategoriesQuery request, CancellationToken ct)
  {
    return await _context.ServiceCategories
      .Where(x => x.TenantId == TenantId)
      .OrderBy(x => x.OrderIndex)
      .Select(x => new ServiceCategoryDto(x.Id, x.Name, x.OrderIndex))
      .ToListAsync(ct);
  }
}