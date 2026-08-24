using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.SetMonthPublication;

/// <summary>
/// Ustawia datę otwarcia zapisów na dany miesiąc. <paramref name="OpensOn"/> = <c>null</c> zamyka
/// miesiąc bezterminowo (do ręcznego otwarcia); data w przyszłości sprawia, że miesiąc otworzy się
/// sam tego dnia — bez potrzeby, by ktokolwiek o tym pamiętał.
///
/// Dotyczy WYŁĄCZNIE rezerwacji online. Personel w panelu wpisuje wizyty w zamkniętym miesiącu
/// normalnie — dlatego to nie jest blokada grafiku, tylko blokada sprzedaży.
/// </summary>
public record SetMonthPublicationCommand(
  Guid EmployeeId,
  int Year,
  int Month,
  DateOnly? OpensOn
) : IRequest<Unit>;

internal class SetMonthPublicationCommandHandler : TenantHandler<SetMonthPublicationCommand, Unit>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public SetMonthPublicationCommandHandler(
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

  public override async Task<Unit> Handle(SetMonthPublicationCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.SetMonthPublication(request.Year, request.Month, request.OpensOn);
    await _uow.SaveChangesAsync(ct);

    return Unit.Value;
  }
}
