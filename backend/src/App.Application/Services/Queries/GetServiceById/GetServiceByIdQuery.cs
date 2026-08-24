using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Services.Dtos;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Queries.GetServiceById;

public record GetServiceByIdQuery(Guid Id) : IRequest<ServiceDto>;

internal class GetServiceByIdHandler : TenantHandler<GetServiceByIdQuery, ServiceDto>
{
  private readonly IApplicationDbContext _context;

  public GetServiceByIdHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<ServiceDto> Handle(GetServiceByIdQuery request, CancellationToken ct)
  {
    var service = await _context.Services
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId && x.Id == request.Id)
      .Select(x => new
      {
        x.Id,
        x.CategoryId,
        x.VatRateId,
        x.Name,
        x.Price,
        x.DurationInMinutes,
        x.PriceMaxAmount,
        x.DurationMinMinutes,
        x.DurationMaxMinutes,
        x.ComboGroup,
        x.HidePrice,
        x.OrderIndex,
        x.IsAddon,
        x.Description,
        Images = x.Images
          .OrderBy(i => i.OrderIndex)
          .Select(i => new ServiceImageDto(i.Url, i.ThumbnailUrl, i.StorageKey, i.OrderIndex))
          .ToList(),
        AddonServiceIds = x.Addons.Select(a => a.AddonServiceId).ToList(),
        EmployeeIds = _context.Employees
          .Where(e => e.Services.Any(es => es.ServiceId == x.Id))
          .Select(e => e.Id)
          .ToList()
      })
      .FirstOrDefaultAsync(ct);

    if (service is null)
    {
      throw new NotFoundException(nameof(Service), request.Id);
    }

    return new ServiceDto(
      service.Id,
      service.CategoryId,
      service.VatRateId,
      service.Name,
      service.Price,
      service.DurationInMinutes,
      service.EmployeeIds,
      service.PriceMaxAmount,
      service.DurationMinMinutes,
      service.DurationMaxMinutes,
      service.ComboGroup,
      service.HidePrice,
      service.OrderIndex,
      service.IsAddon,
      service.AddonServiceIds,
      service.Description,
      service.Images);
  }
}