using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.DeleteEmployee;

public record DeleteEmployeeCommand(Guid Id) : IRequest;

internal class DeleteEmployeeCommandHandler : TenantHandler<DeleteEmployeeCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IDeletionService _deletionService;
  private readonly IUnitOfWork _uow;

  public DeleteEmployeeCommandHandler(IEmployeeRepository repository, IDeletionService deletionService, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _deletionService = deletionService;
    _uow = uow;
  }

  public override async Task Handle(DeleteEmployeeCommand request, CancellationToken ct)
  {
    var employee = await _repository.GetByIdAsync(request.Id)
        ?? throw new NotFoundException(nameof(Employee), request.Id);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    await _deletionService.DeleteAsync(employee, ct);
    await _uow.SaveChangesAsync(ct);
  }
}
