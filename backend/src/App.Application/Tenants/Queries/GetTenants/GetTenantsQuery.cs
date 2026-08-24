using App.Application.Common.Interfaces;
using App.Application.Tenants.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Queries.GetTenants;

public record GetTenantsQuery : IRequest<List<TenantDto>>;

public class GetTenantsQueryHandler : IRequestHandler<GetTenantsQuery, List<TenantDto>>
{
  private readonly IApplicationDbContext _context;

  public GetTenantsQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  // Backstop przeciw nieograniczonemu materializowaniu listy (analogicznie do GetTenantsAdmin).
  private const int MaxResults = 1000;

  public async Task<List<TenantDto>> Handle(GetTenantsQuery request, CancellationToken ct)
  {
    return await _context.Tenants
        .AsNoTracking()
        .OrderBy(t => t.Name)
        .Take(MaxResults)
        .Select(t => new TenantDto(
          t.Id,
          t.Name,
          t.Slug,
          t.CustomerVerificationChannel,
          t.AppointmentSlotStepMinutes,
          t.TimeZoneId,
          t.Currency,
          t.BookingAccessPolicy,
          t.AppointmentConfirmationMode))
        .ToListAsync(ct);
  }
}
