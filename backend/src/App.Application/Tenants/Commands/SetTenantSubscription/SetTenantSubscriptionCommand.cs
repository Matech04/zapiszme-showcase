using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainSubscription = App.Domain.Aggregates.TenantAggregate.Subscription;

namespace App.Application.Tenants.Commands.SetTenantSubscription;

/// <summary>
/// Admin-only override stanu subskrypcji. Nie używaj w normalnym biznes-flow
/// (Activate, ChangeSeats itd. są właściwymi ścieżkami) — to mechanizm naprawy
/// stanu / migracji ręcznej dla obsługi klienta.
/// </summary>
public record SetTenantSubscriptionCommand(
    Guid TenantId,
    SubscriptionStatus Status,
    int Seats,
    bool IsFoundingMember,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEndsAt
) : IRequest;

public class SetTenantSubscriptionCommandHandler : IRequestHandler<SetTenantSubscriptionCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public SetTenantSubscriptionCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task Handle(SetTenantSubscriptionCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

    tenant.SetSubscription(DomainSubscription.AdminReset(
      request.Status,
      request.Seats,
      request.IsFoundingMember,
      request.TrialEndsAt,
      request.CurrentPeriodEndsAt));

    await _uow.SaveChangesAsync(ct);
  }
}
