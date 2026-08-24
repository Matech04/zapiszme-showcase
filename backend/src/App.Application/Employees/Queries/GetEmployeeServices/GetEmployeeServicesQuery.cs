using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetEmployeeServices;

public record GetEmployeeServicesQuery(Guid EmployeeId) : IRequest<List<EmployeeServiceDto>>;

internal class GetEmployeeServicesQueryHandler : TenantHandler<GetEmployeeServicesQuery, List<EmployeeServiceDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetEmployeeServicesQueryHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }

  public override async Task<List<EmployeeServiceDto>> Handle(GetEmployeeServicesQuery request, CancellationToken ct)
  {
    // Godziny pracy / dni specjalne / usługi kolegi — odczyt szerszy niż profil, ale ograniczony
    // do tego salonu i do polityki widoczności kalendarza.
    await _access.EnsureCanReadEmployeeCalendarDataAsync(request.EmployeeId, ct);

    var employee = await _context.Employees
        .AsNoTracking()
        .Include(e => e.Services)
        .Where(e => e.Id == request.EmployeeId && e.TenantId == TenantId)
        .FirstOrDefaultAsync(ct);

    if (employee == null)
    {
      throw new NotFoundException(nameof(Employee), request.EmployeeId);
    }

    var assignments = employee.Services.ToList();
    var assignedIds = assignments.Select(s => s.ServiceId).ToList();

    // Bez tego kolejność wynika z tego, jak baza odda wiersze EmployeeServices — jest dowolna
    // i potrafi się zmienić po edycji przypisań. Konsumenci pokazują usługi wprost, więc
    // sortujemy tak jak katalog (GetServicesQuery): OrderIndex, potem Name.
    var catalogOrder = await _context.Services
        .AsNoTracking()
        .Where(s => assignedIds.Contains(s.Id))
        .OrderBy(s => s.OrderIndex)
        .ThenBy(s => s.Name)
        .Select(s => s.Id)
        .ToListAsync(ct);

    var rankByServiceId = catalogOrder
        .Select((id, index) => (id, index))
        .ToDictionary(x => x.id, x => x.index);

    // Usługi nieobjęte katalogiem (filtr globalny odcina nieaktywne) zostają na liście,
    // tylko lądują na końcu — gubienie przypisań byłoby zmianą semantyki, nie sortowaniem.
    return assignments
        .OrderBy(s => rankByServiceId.TryGetValue(s.ServiceId, out var rank) ? rank : int.MaxValue)
        .Select(s => new EmployeeServiceDto(s.ServiceId, s.CustomDuration, s.CustomPrice))
        .ToList();
  }
}