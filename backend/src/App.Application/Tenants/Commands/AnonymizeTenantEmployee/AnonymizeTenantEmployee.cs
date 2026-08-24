using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.AnonymizeTenantEmployee;

/// <summary>
/// RODO art. 17 dla personelu: admin trwale anonimizuje dane osobowe pracownicy wskazanego salonu
/// (<see cref="Employee.Anonymize"/>) — w przeciwieństwie do soft-delete, który zostawia PII w bazie.
/// Cross-tenant (admin bez kontekstu tenanta), więc jawnie egzekwujemy uprawnienie platformowe.
/// </summary>
public record AnonymizeTenantEmployeeCommand(Guid TenantId, Guid EmployeeId) : IRequest;

internal sealed class AnonymizeTenantEmployeeCommandHandler : IRequestHandler<AnonymizeTenantEmployeeCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ICurrentUserAccessor _currentUser;

  public AnonymizeTenantEmployeeCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentUserAccessor currentUser)
  {
    _context = context;
    _uow = uow;
    _currentUser = currentUser;
  }

  public async Task Handle(AnonymizeTenantEmployeeCommand request, CancellationToken ct)
  {
    // Defense-in-depth: operacja cross-tenant — nie polegaj wyłącznie na atrybucie kontrolera.
    if (!_currentUser.IsSystemAdmin)
    {
      throw new ForbiddenAccessException(
        "Operacja dostępna tylko dla administratora platformy.", ErrorCodes.Forbidden);
    }

    var employee = await _context.Employees
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == request.TenantId, ct)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    employee.Anonymize();
    await _uow.SaveChangesAsync(ct);
  }
}
