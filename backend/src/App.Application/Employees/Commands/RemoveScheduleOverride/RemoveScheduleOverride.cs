using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.RemoveScheduleOverride;

public record RemoveScheduleOverrideCommand(Guid EmployeeId, DateOnly Date) : IRequest;

internal class RemoveScheduleOverrideCommandHandler : TenantHandler<RemoveScheduleOverrideCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public RemoveScheduleOverrideCommandHandler(
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

  public override async Task Handle(RemoveScheduleOverrideCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.RemoveScheduleOverride(request.Date);
    await _uow.SaveChangesAsync(ct);
  }
}
