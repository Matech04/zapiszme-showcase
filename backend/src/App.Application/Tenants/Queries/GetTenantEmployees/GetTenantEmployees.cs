using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Queries.GetTenantEmployees;

public record TenantEmployeeDto(
  Guid Id,
  string FirstName,
  string LastName,
  string Email,
  bool HasAccount);

/// <summary>
/// Admin (SystemAdmin): lista aktywnych pracowników wskazanego salonu — niezależnie od kontekstu
/// tenanta (cross-tenant, <c>IgnoreQueryFilters</c> + jawne zawężenie po <c>TenantId</c>).
/// <c>HasAccount</c> = pracownik ma powiązane konto logowania (Owner/Manager/Employee), w odróżnieniu
/// od czystego zasobu kalendarza.
/// </summary>
public record GetTenantEmployeesQuery(Guid TenantId) : IRequest<List<TenantEmployeeDto>>;

internal sealed class GetTenantEmployeesQueryHandler
    : IRequestHandler<GetTenantEmployeesQuery, List<TenantEmployeeDto>>
{
  private readonly IApplicationDbContext _context;

  public GetTenantEmployeesQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<List<TenantEmployeeDto>> Handle(GetTenantEmployeesQuery request, CancellationToken ct)
  {
    return await _context.Employees
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(e => e.TenantId == request.TenantId && e.IsActive)
        .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
        .Select(e => new TenantEmployeeDto(
            e.Id,
            e.FirstName,
            e.LastName,
            e.Email,
            e.UserId != null))
        .ToListAsync(ct);
  }
}
