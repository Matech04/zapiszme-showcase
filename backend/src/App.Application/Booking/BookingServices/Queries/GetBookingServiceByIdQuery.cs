using App.Application.Booking.BookingServices.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingServices.Queries;

public record GetBookingServiceByIdQuery(Guid Id) : IRequest<BookingServiceDto>;

internal class GetBookingServiceByIdQueryHandler : TenantHandler<GetBookingServiceByIdQuery, BookingServiceDto>
{
  private readonly IApplicationDbContext _context;

  public GetBookingServiceByIdQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<BookingServiceDto> Handle(GetBookingServiceByIdQuery request, CancellationToken ct)
  {
    var service = await _context.Services
        .AsNoTracking()
        .Where(x => x.TenantId == TenantId && x.Id == request.Id)
        .Select(x => new BookingServiceDto(
            x.Id, x.CategoryId, x.Name, x.Price, x.DurationInMinutes,
            x.PriceMaxAmount, x.DurationMinMinutes, x.DurationMaxMinutes, x.ComboGroup,
            x.HidePrice, x.OrderIndex,
            x.IsAddon, x.Addons.Select(a => a.AddonServiceId).ToList(),
            x.Description,
            x.Images
              .OrderBy(i => i.OrderIndex)
              .Select(i => new BookingServiceImageDto(i.Url, i.ThumbnailUrl, i.OrderIndex))
              .ToList()))
        .FirstOrDefaultAsync(ct);

    return service ?? throw new NotFoundException(nameof(Service), request.Id);
  }
}
