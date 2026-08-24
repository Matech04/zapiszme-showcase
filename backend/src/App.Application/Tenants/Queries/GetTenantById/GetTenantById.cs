using App.Application.Common.Interfaces;
using App.Application.Tenants.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;

namespace App.Application.Tenants.Queries.GetTenantById;

public record GetTenantByIdQuery(Guid Id) : IRequest<TenantDto>;

public class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, TenantDto>
{
  private readonly IApplicationDbContext _context;

  public GetTenantByIdQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<TenantDto> Handle(GetTenantByIdQuery request, CancellationToken ct)
  {

    // Materializujemy encję i mapujemy w pamięci — projekcja EF (drzewo wyrażeń) nie dopuszcza
    // named-arg „out of position", a CustomDomain to opcjonalny parametr na końcu TenantDto.
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == request.Id, ct);

    if (tenant == null)
    {
      throw new NotFoundException(nameof(Tenant), request.Id);
    }

    return new TenantDto(
      tenant.Id,
      tenant.Name,
      tenant.Slug,
      tenant.CustomerVerificationChannel,
      tenant.AppointmentSlotStepMinutes,
      tenant.TimeZoneId,
      tenant.Currency,
      tenant.BookingAccessPolicy,
      tenant.AppointmentConfirmationMode,
      CustomDomain: tenant.CustomDomain);
  }
}
