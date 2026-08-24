using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.SetEmployeeScheduleActive;

public record SetEmployeeScheduleActiveCommand(Guid EmployeeId, Guid ScheduleId, bool IsActive) : IRequest;

internal class SetEmployeeScheduleActiveCommandHandler : TenantHandler<SetEmployeeScheduleActiveCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public SetEmployeeScheduleActiveCommandHandler(
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

  public override async Task Handle(SetEmployeeScheduleActiveCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.SetScheduleActive(request.ScheduleId, request.IsActive);
    await _uow.SaveChangesAsync(ct);
  }
}
