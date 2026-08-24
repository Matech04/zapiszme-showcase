using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetEmployeeById;

public record GetEmployeeByIdQuery(Guid Id) : IRequest<EmployeeDto>;

internal class GetEmployeeByIdQueryHandler : TenantHandler<GetEmployeeByIdQuery, EmployeeDto>
{
  private readonly IApplicationDbContext _context;

  public GetEmployeeByIdQueryHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<EmployeeDto> Handle(GetEmployeeByIdQuery request, CancellationToken ct)
  {
    var employee = await _context.Employees
        .AsNoTracking()
        .Where(x => x.TenantId == TenantId)
        .FirstOrDefaultAsync(e => e.Id == request.Id, ct)
        ?? throw new NotFoundException(nameof(Employee), request.Id);

    var accounts = employee.UserId is Guid uid
        ? await EmployeeAccountLookup.LoadAsync(_context, new[] { uid }, ct)
        : new Dictionary<Guid, EmployeeAccountLookup.AccountInfo>();

    var info = employee.UserId is Guid id && accounts.TryGetValue(id, out var a) ? a : default;

    return new EmployeeDto(
        employee.Id, employee.UserId, employee.FirstName, employee.LastName, employee.Email,
        employee.Specialization, employee.SlotGenerationMode, info.Role, info.InvitePending);
  }
}