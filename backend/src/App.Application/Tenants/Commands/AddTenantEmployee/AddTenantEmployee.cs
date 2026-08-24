using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.AddTenantEmployee;

/// <summary>
/// Admin (SystemAdmin): dodaje pracownika do istniejącego salonu jako ZASÓB kalendarza
/// (<c>Employee.UserId = null</c> — bez logowania). Konto logowania można dopisać później
/// (<c>Employee.LinkToUser</c> + zaproszenie). Działa cross-tenant: admin nie ma kontekstu tenanta,
/// więc write-side <c>TenantViolation</c> jest pomijany (świadomy wyjątek, jak przy create-salon).
/// </summary>
public record AddTenantEmployeeCommand(Guid TenantId, string FirstName, string LastName, string Email)
    : IRequest<Guid>;

internal sealed class AddTenantEmployeeCommandHandler : IRequestHandler<AddTenantEmployeeCommand, Guid>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ICurrentUserAccessor _currentUser;

  public AddTenantEmployeeCommandHandler(IApplicationDbContext context, IUnitOfWork uow, ICurrentUserAccessor currentUser)
  {
    _context = context;
    _uow = uow;
    _currentUser = currentUser;
  }

  public async Task<Guid> Handle(AddTenantEmployeeCommand request, CancellationToken ct)
  {
    // Defense-in-depth: zapis cross-tenant (admin bez kontekstu tenanta omija write-side TenantViolation),
    // więc jawnie wymagamy roli platformowego administratora — nie polegaj tylko na atrybucie kontrolera.
    if (!_currentUser.IsSystemAdmin)
    {
      throw new ForbiddenAccessException(
        "Operacja dostępna tylko dla administratora platformy.", ErrorCodes.Forbidden);
    }

    var tenantExists = await _context.Tenants
        .IgnoreQueryFilters()
        .AnyAsync(t => t.Id == request.TenantId, ct);
    if (!tenantExists)
    {
      throw new NotFoundException(nameof(Tenant), request.TenantId);
    }

    var employee = new Employee(request.TenantId, userId: null, request.FirstName, request.LastName, request.Email);
    _context.Employees.Add(employee);
    await _uow.SaveChangesAsync(ct);

    return employee.Id;
  }
}
