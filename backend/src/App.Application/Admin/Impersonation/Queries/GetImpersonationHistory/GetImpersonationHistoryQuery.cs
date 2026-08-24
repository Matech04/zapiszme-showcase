using App.Application.Admin.Impersonation.Dtos;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.Impersonation.Queries.GetImpersonationHistory;

/// <summary>Admin-only: dziennik audytu sesji wsparcia (opcjonalnie filtrowany po tenancie).</summary>
public record GetImpersonationHistoryQuery(Guid? TenantId = null, int Take = 100)
  : IRequest<List<ImpersonationHistoryItemDto>>;

public class GetImpersonationHistoryQueryHandler
  : IRequestHandler<GetImpersonationHistoryQuery, List<ImpersonationHistoryItemDto>>
{
  private readonly IApplicationDbContext _context;

  public GetImpersonationHistoryQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<List<ImpersonationHistoryItemDto>> Handle(GetImpersonationHistoryQuery r, CancellationToken ct)
  {
    var take = Math.Clamp(r.Take, 1, 500);

    var q = _context.ImpersonationSessions.AsNoTracking();
    if (r.TenantId is { } tenantId)
    {
      q = q.Where(s => s.TargetTenantId == tenantId);
    }

    // Join do Tenants (bez query filtra) dla nazwy salonu.
    return await q
      .OrderByDescending(s => s.StartedAtUtc)
      .Take(take)
      .Join(
        _context.Tenants,
        s => s.TargetTenantId,
        t => t.Id,
        (s, t) => new ImpersonationHistoryItemDto(
          s.Id,
          s.AdminUserId,
          s.TargetTenantId,
          t.Name,
          s.Reason,
          s.IsReadOnly,
          s.StartedAtUtc,
          s.ExpiresAtUtc,
          s.EndedAtUtc,
          s.IpAddress))
      .ToListAsync(ct);
  }
}
