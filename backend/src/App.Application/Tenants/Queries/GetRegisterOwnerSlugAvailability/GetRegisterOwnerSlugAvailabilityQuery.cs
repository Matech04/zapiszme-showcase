using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Queries.GetRegisterOwnerSlugAvailability;

/// <summary>
/// Wynik sprawdzenia, czy podany slug salonu jest wolny (brak tenanta o tym slugu).
/// </summary>
public sealed record SalonSlugAvailabilityResult(bool Available);

/// <summary>
/// Sprawdza dostępność sluga. Slug musi przejść walidację FluentValidation.
/// Rejestracja: <see cref="ExcludeCurrentTenant"/> = false — slug zajęty, jeśli istnieje dowolny tenant.
/// Ustawienia salonu: wyłącznie z zaufanego endpointu staff — <see cref="ExcludeCurrentTenant"/> = true;
/// wykluczenie bieżącego tenanta pochodzi z <see cref="ICurrentTenantService"/> (nie z klienta).
/// </summary>
public sealed record GetRegisterOwnerSlugAvailabilityQuery(string Slug, bool ExcludeCurrentTenant = false)
  : IRequest<SalonSlugAvailabilityResult>;

public sealed class GetRegisterOwnerSlugAvailabilityQueryHandler
  : IRequestHandler<GetRegisterOwnerSlugAvailabilityQuery, SalonSlugAvailabilityResult>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentTenantService _currentTenantService;

  public GetRegisterOwnerSlugAvailabilityQueryHandler(
    IApplicationDbContext context,
    ICurrentTenantService currentTenantService)
  {
    _context = context;
    _currentTenantService = currentTenantService;
  }

  public async Task<SalonSlugAvailabilityResult> Handle(
    GetRegisterOwnerSlugAvailabilityQuery request,
    CancellationToken cancellationToken)
  {
    var trimmed = request.Slug.Trim();
    bool exists;
    if (request.ExcludeCurrentTenant)
    {
      var tenantId = _currentTenantService.TenantId ?? throw new NoTenantHeader();
      exists = await _context.Tenants
        .AsNoTracking()
        .AnyAsync(t => t.Slug == trimmed && t.Id != tenantId, cancellationToken);
    }
    else
    {
      exists = await _context.Tenants
        .AsNoTracking()
        .AnyAsync(t => t.Slug == trimmed, cancellationToken);
    }

    return new SalonSlugAvailabilityResult(Available: !exists);
  }
}
