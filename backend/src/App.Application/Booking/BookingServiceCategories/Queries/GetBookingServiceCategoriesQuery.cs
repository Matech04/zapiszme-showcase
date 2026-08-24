using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.ServiceCategories.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingServiceCategories.Queries;

public record GetBookingServiceCategoriesQuery : IRequest<List<ServiceCategoryDto>>;

internal sealed class GetBookingServiceCategoriesQueryHandler
    : TenantHandler<GetBookingServiceCategoriesQuery, List<ServiceCategoryDto>>
{
  private readonly IApplicationDbContext _context;

  public GetBookingServiceCategoriesQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<ServiceCategoryDto>> Handle(
      GetBookingServiceCategoriesQuery request,
      CancellationToken ct)
  {
    return await _context.ServiceCategories
        .AsNoTracking()
        .Where(x => x.TenantId == TenantId)
        .OrderBy(x => x.OrderIndex)
        .Select(x => new ServiceCategoryDto(x.Id, x.Name, x.OrderIndex))
        .ToListAsync(ct);
  }
}
