using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.SetTenantCustomDomain;

/// <summary>
/// Admin: ustawia/czyści white-label domenę tenanta (np. <c>salon-przyklad.pl</c>). Pusta → <c>null</c>
/// (wyłącza white-label). Po zapisie odświeża <see cref="ICustomDomainRegistry"/>, żeby resolve-host /
/// CORS / On-Demand TLS działały od razu, bez czekania na cykliczny refresh.
/// </summary>
public record SetTenantCustomDomainCommand(Guid TenantId, string? CustomDomain) : IRequest;

internal sealed class SetTenantCustomDomainCommandHandler : IRequestHandler<SetTenantCustomDomainCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ICustomDomainRegistry _registry;
  private readonly ICurrentUserAccessor _currentUser;

  public SetTenantCustomDomainCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICustomDomainRegistry registry,
    ICurrentUserAccessor currentUser)
  {
    _context = context;
    _uow = uow;
    _registry = registry;
    _currentUser = currentUser;
  }

  public async Task Handle(SetTenantCustomDomainCommand request, CancellationToken ct)
  {
    // Defense-in-depth: zapis cross-tenant — jawnie wymagamy roli platformowego administratora.
    if (!_currentUser.IsSystemAdmin)
    {
      throw new ForbiddenAccessException(
        "Operacja dostępna tylko dla administratora platformy.", ErrorCodes.Forbidden);
    }

    var tenant = await _context.Tenants
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

    var normalized = string.IsNullOrWhiteSpace(request.CustomDomain)
        ? null
        : request.CustomDomain.Trim().ToLowerInvariant();

    if (normalized != null)
    {
      var takenByOther = await _context.Tenants
          .IgnoreQueryFilters()
          .AnyAsync(t => t.Id != request.TenantId && t.CustomDomain == normalized, ct);
      if (takenByOther)
      {
        throw new CustomDomainAlreadyAssignedException(normalized);
      }
    }

    tenant.SetCustomDomain(normalized);
    await _uow.SaveChangesAsync(ct);

    // Snapshot zasila CORS, resolve-host i ask On-Demand TLS — odśwież po zmianie domeny.
    await _registry.RefreshAsync(ct);
  }
}
