using App.Application.Admin.Impersonation.Dtos;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.Impersonation.Queries.GetCurrentImpersonation;

/// <summary>
/// Stan aktywnej sesji wsparcia wskazanej przez cookie (id przekazuje controller).
/// Zwraca null, gdy sesja nie istnieje, jest zakończona lub wygasła — dashboard ukrywa baner.
/// </summary>
public record GetCurrentImpersonationQuery(Guid SessionId) : IRequest<ImpersonationStatusDto?>;

public class GetCurrentImpersonationQueryHandler : IRequestHandler<GetCurrentImpersonationQuery, ImpersonationStatusDto?>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;

  public GetCurrentImpersonationQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
  {
    _context = context;
    _timeProvider = timeProvider;
  }

  public async Task<ImpersonationStatusDto?> Handle(GetCurrentImpersonationQuery r, CancellationToken ct)
  {
    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    var session = await _context.ImpersonationSessions
      .AsNoTracking()
      .FirstOrDefaultAsync(s => s.Id == r.SessionId, ct);

    if (session is null || !session.IsActive(nowUtc))
    {
      return null;
    }

    // Tenant bez query filtra — odczyt bezpośredni.
    var tenant = await _context.Tenants
      .AsNoTracking()
      .Where(t => t.Id == session.TargetTenantId)
      .Select(t => new { t.Name, t.Slug })
      .FirstOrDefaultAsync(ct);

    if (tenant is null)
    {
      return null;
    }

    return new ImpersonationStatusDto(
      session.Id,
      session.TargetTenantId,
      tenant.Name,
      tenant.Slug,
      session.Reason,
      session.IsReadOnly,
      session.ExpiresAtUtc,
      (int)session.RemainingTime(nowUtc).TotalSeconds);
  }
}
