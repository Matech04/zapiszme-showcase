using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.CreateEmployee;

public record CreateEmployeeCommand(Guid? UserId, string FirstName, string LastName, string Email) : IRequest<Guid>;

internal class CreateEmployeeCommandHandler : TenantHandler<CreateEmployeeCommand, Guid>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;

  public CreateEmployeeCommandHandler(IEmployeeRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(CreateEmployeeCommand request, CancellationToken ct)
  {
    var employee = new Employee(TenantId, request.UserId, request.FirstName, request.LastName, request.Email);

    await _repository.AddAsync(employee);
    await _uow.SaveChangesAsync(ct);

    return employee.Id;
  }
}
