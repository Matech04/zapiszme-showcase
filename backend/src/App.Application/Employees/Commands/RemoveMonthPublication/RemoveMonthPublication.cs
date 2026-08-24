using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.RemoveMonthPublication;

/// <summary>
/// Usuwa jawną decyzję o publikacji miesiąca — miesiąc wraca pod domyślny horyzont rezerwacji.
/// To NIE zamknięcie: po tej operacji miesiąc jest widoczny, o ile mieści się w horyzoncie.
/// </summary>
public record RemoveMonthPublicationCommand(Guid EmployeeId, int Year, int Month) : IRequest<Unit>;

internal class RemoveMonthPublicationCommandHandler : TenantHandler<RemoveMonthPublicationCommand, Unit>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public RemoveMonthPublicationCommandHandler(
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

  public override async Task<Unit> Handle(RemoveMonthPublicationCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.ClearMonthPublication(request.Year, request.Month);
    await _uow.SaveChangesAsync(ct);

    return Unit.Value;
  }
}
