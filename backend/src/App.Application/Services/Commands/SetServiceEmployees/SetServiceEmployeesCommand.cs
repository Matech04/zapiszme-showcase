using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Commands.SetServiceEmployees;

public record SetServiceEmployeesCommand(Guid ServiceId, List<Guid> EmployeeIds) : IRequest;

internal class SetServiceEmployeesHandler : TenantHandler<SetServiceEmployeesCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public SetServiceEmployeesHandler(
      IApplicationDbContext context,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task Handle(SetServiceEmployeesCommand request, CancellationToken ct)
  {
    var service = await _context.Services
      .FirstOrDefaultAsync(s => s.Id == request.ServiceId, ct)
      ?? throw new NotFoundException(nameof(Service), request.ServiceId);

    var requestedIds = request.EmployeeIds?.Distinct().ToList() ?? new List<Guid>();

    if (requestedIds.Count > 0)
    {
      var existingIds = await _context.Employees
        .Where(e => requestedIds.Contains(e.Id))
        .Select(e => e.Id)
        .ToListAsync(ct);

      var missing = requestedIds.Except(existingIds).ToList();
      if (missing.Count > 0)
      {
        throw new NotFoundException(nameof(Employee), missing[0]);
      }
    }

    var affectedEmployees = await _context.Employees
      .Include(e => e.Services)
      .Where(e => e.Services.Any(s => s.ServiceId == request.ServiceId) || requestedIds.Contains(e.Id))
      .ToListAsync(ct);

    foreach (var emp in affectedEmployees)
    {
      var hasService = emp.Services.Any(s => s.ServiceId == request.ServiceId);
      var shouldHave = requestedIds.Contains(emp.Id);

      if (shouldHave && !hasService)
      {
        // Przypisanie z multi-selecta w formularzu usługi NIE jest override'em per-pracownik —
        // ma dziedziczyć czas i cenę z katalogu (null), żeby późniejsza edycja usługi propagowała się
        // automatycznie. Świadomy override ustawia się dopiero w formularzu pracownika
        // (AssignServiceCommand / UpdateEmployeeServiceCommand).
        emp.AssignService(TenantId, request.ServiceId, customDuration: null, customPrice: null);
      }
      else if (!shouldHave && hasService)
      {
        emp.UnassignService(request.ServiceId);
      }
    }

    await _uow.SaveChangesAsync(ct);
  }
}
