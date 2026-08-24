using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Subscription.Commands.ChangeSeats;

/// <summary>
/// Zmienia liczbę stanowisk w bieżącej subskrypcji salonu. Soft limit — dowolna wartość ≥ 1
/// jest akceptowana, nowa cena obowiązuje od następnego cyklu rozliczeniowego (front pokazuje
/// klientowi delta w cenie zanim potwierdzi zmianę).
/// </summary>
public record ChangeSeatsCommand(int NewSeats) : IRequest<ChangeSeatsResult>;

public record ChangeSeatsResult(
    int Seats,
    int MonthlyPriceInGrosze,
    int MonthlySmsAllowance
);

internal sealed class ChangeSeatsCommandHandler : TenantHandler<ChangeSeatsCommand, ChangeSeatsResult>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public ChangeSeatsCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task<ChangeSeatsResult> Handle(ChangeSeatsCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .FirstOrDefaultAsync(t => t.Id == TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), TenantId);

    tenant.Subscription.ChangeSeats(request.NewSeats);
    await _uow.SaveChangesAsync(ct);

    return new ChangeSeatsResult(
      tenant.Subscription.Seats,
      tenant.Subscription.MonthlyPriceInGrosze,
      tenant.Subscription.MonthlySmsAllowance);
  }
}
