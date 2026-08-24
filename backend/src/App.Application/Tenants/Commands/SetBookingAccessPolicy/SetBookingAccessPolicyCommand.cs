using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Tenants.Commands.SetBookingAccessPolicy;

/// <summary>
/// Ustawia politykę dostępu do publicznej rezerwacji online dla obecnego tenanta.
/// </summary>
public record SetBookingAccessPolicyCommand(BookingAccessPolicy Policy) : IRequest;

internal class SetBookingAccessPolicyCommandHandler : TenantHandler<SetBookingAccessPolicyCommand>
{
  private readonly ITenantRepository _repository;
  private readonly IUnitOfWork _uow;

  public SetBookingAccessPolicyCommandHandler(
    ITenantRepository repository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(SetBookingAccessPolicyCommand request, CancellationToken ct)
  {
    var tenant = await _repository.GetByIdAsync(TenantId)
        ?? throw new NotFoundException(nameof(Tenant), TenantId);

    tenant.Update(tenant.Name, tenant.Slug, bookingAccessPolicy: request.Policy);
    _repository.Update(tenant);
    await _uow.SaveChangesAsync(ct);
  }
}
