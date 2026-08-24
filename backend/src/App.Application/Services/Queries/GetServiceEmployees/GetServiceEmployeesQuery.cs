using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Queries.GetServiceEmployees;

public record GetServiceEmployeesQuery(Guid ServiceId) : IRequest<List<Guid>>;

internal class GetServiceEmployeesHandler : TenantHandler<GetServiceEmployeesQuery, List<Guid>>
{
  private readonly IApplicationDbContext _context;

  public GetServiceEmployeesHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<Guid>> Handle(GetServiceEmployeesQuery request, CancellationToken ct)
  {
    var serviceExists = await _context.Services
      .AsNoTracking()
      .AnyAsync(s => s.Id == request.ServiceId, ct);

    if (!serviceExists)
    {
      throw new NotFoundException(nameof(Service), request.ServiceId);
    }

    return await _context.Employees
      .AsNoTracking()
      .Where(e => e.Services.Any(s => s.ServiceId == request.ServiceId))
      .Select(e => e.Id)
      .ToListAsync(ct);
  }
}
