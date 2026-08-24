using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Employees.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetEmployees;

public record GetEmployeesQuery : IRequest<List<EmployeeDto>>;

internal class GetEmployeesQueryHandler : TenantHandler<GetEmployeesQuery, List<EmployeeDto>>
{
  private readonly IApplicationDbContext _context;

  public GetEmployeesQueryHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<EmployeeDto>> Handle(GetEmployeesQuery request, CancellationToken ct)
  {
    var employees = await _context.Employees
        .AsNoTracking()
        .Where(x => x.TenantId == TenantId && x.IsBookable)
        .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
        .Select(e => new
        {
          e.Id,
          e.UserId,
          e.FirstName,
          e.LastName,
          e.Email,
          e.Specialization,
          e.SlotGenerationMode,
        })
        .ToListAsync(ct);

    var userIds = employees
        .Where(e => e.UserId != null)
        .Select(e => e.UserId!.Value)
        .Distinct()
        .ToList();

    var accounts = await EmployeeAccountLookup.LoadAsync(_context, userIds, ct);

    return employees
        .Select(e =>
        {
          var info = e.UserId is Guid uid && accounts.TryGetValue(uid, out var a)
              ? a
              : default;
          return new EmployeeDto(
              e.Id, e.UserId, e.FirstName, e.LastName, e.Email, e.Specialization, e.SlotGenerationMode,
              info.Role, info.InvitePending);
        })
        .ToList();
  }
}