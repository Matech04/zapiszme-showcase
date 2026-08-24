using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.UpdateEmployeeService;

/// <summary>
/// Aktualizuje override usługi pracownika. <paramref name="CustomDuration"/>/<paramref name="Amount"/>
/// opcjonalne — <c>null</c> = wyczyść override (wróć do dziedziczenia z katalogu).
/// </summary>
public record UpdateEmployeeServiceCommand(Guid EmployeeId, Guid ServiceId, int? CustomDuration = null, decimal? Amount = null, string? Currency = null) : IRequest;

internal class UpdateEmployeeServiceCommandHandler : TenantHandler<UpdateEmployeeServiceCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public UpdateEmployeeServiceCommandHandler(
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

  public override async Task Handle(UpdateEmployeeServiceCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    // null = wyczyść override (dziedzicz z katalogu). Cena tylko gdy podano kwotę i walutę.
    Money? price = request.Amount is { } amount && !string.IsNullOrWhiteSpace(request.Currency)
        ? new Money(amount, request.Currency)
        : null;
    employee.UpdateService(request.ServiceId, request.CustomDuration, price);
    await _uow.SaveChangesAsync(ct);
  }
}