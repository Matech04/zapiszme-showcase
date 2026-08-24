using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.MarkFoundingMember;

/// <summary>
/// Admin-only — oznacza salon jako Founding Member (cena bazowa 49 zł zamiast 79 zł, dożywotnio).
/// Przeznaczone dla pierwszych ~10 klientek. NIE używaj poza endpointem admin —
/// flaga bezpośrednio wpływa na cenę i można jej nadużyć.
/// </summary>
public record MarkFoundingMemberCommand(Guid TenantId) : IRequest;

public class MarkFoundingMemberCommandHandler : IRequestHandler<MarkFoundingMemberCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public MarkFoundingMemberCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task Handle(MarkFoundingMemberCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

    tenant.Subscription.MarkAsFoundingMember();
    await _uow.SaveChangesAsync(ct);
  }
}
