using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.AssignService;

/// <summary>
/// Przypisuje usługę pracownikowi. <paramref name="CustomDuration"/>/<paramref name="Amount"/> są
/// OPCJONALNE — <c>null</c> oznacza „dziedzicz z katalogu usługi" (czas/cena nie są nadpisywane
/// per-pracownik). Wartości niepuste to świadomy override.
/// </summary>
public record AssignServiceCommand(Guid EmployeeId, Guid ServiceId, int? CustomDuration = null, decimal? Amount = null, string? Currency = null) : IRequest;

internal class AssignServiceCommandHandler : TenantHandler<AssignServiceCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public AssignServiceCommandHandler(
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

  public override async Task Handle(AssignServiceCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    // null = dziedzicz z katalogu (brak override). Cena tylko gdy podano kwotę i walutę.
    Money? price = request.Amount is { } amount && !string.IsNullOrWhiteSpace(request.Currency)
        ? new Money(amount, request.Currency)
        : null;
    employee.AssignService(TenantId, request.ServiceId, request.CustomDuration, price);
    await _uow.SaveChangesAsync(ct);
  }
}