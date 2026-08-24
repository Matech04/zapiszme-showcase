using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.RemoveEmployeeLeave;

public record RemoveEmployeeLeaveCommand(Guid EmployeeId, Guid LeaveId) : IRequest;

internal class RemoveEmployeeLeaveCommandHandler : TenantHandler<RemoveEmployeeLeaveCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public RemoveEmployeeLeaveCommandHandler(
    IEmployeeRepository repository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
    _access = access;
  }

  public override async Task Handle(RemoveEmployeeLeaveCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdWithLeavesAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    var leave = employee.Leaves.FirstOrDefault(l => l.Id == request.LeaveId)
        ?? throw new NotFoundException(nameof(EmployeeLeave), request.LeaveId);

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (leave.DateRange.StartDate <= today)
    {
      throw new OnlyUpcomingLeaveMayBeRemovedException();
    }

    employee.RemoveLeave(request.LeaveId);
    await _uow.SaveChangesAsync(ct);
  }
}