using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.UpdateEmployee;

public record UpdateEmployeeCommand(Guid Id, string FirstName, string LastName, string? Specialization) : IRequest;

public record UpdateEmployeeRequest(string FirstName, string LastName, string? Specialization);

internal class UpdateEmployeeCommandHandler : TenantHandler<UpdateEmployeeCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public UpdateEmployeeCommandHandler(
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

  public override async Task Handle(UpdateEmployeeCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.Id);

    var employee = await _repository.GetByIdAsync(request.Id)
        ?? throw new NotFoundException(nameof(Employee), request.Id);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.Update(request.FirstName, request.LastName, request.Specialization);

    await _uow.SaveChangesAsync(ct);
  }
}