using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Customers.Commands.SetCustomerWhitelist;

/// <summary>
/// Ustawia flagę `IsWhitelisted` dla pojedynczego klienta. Wykorzystywane w trybie
/// salonu „tylko zaproszeni” (`BookingAccessPolicy.InviteOnly`).
/// </summary>
public record SetCustomerWhitelistCommand(Guid CustomerId, bool IsWhitelisted) : IRequest;

internal class SetCustomerWhitelistCommandHandler : TenantHandler<SetCustomerWhitelistCommand>
{
  private readonly ICustomerRepository _customerRepository;
  private readonly IUnitOfWork _uow;

  public SetCustomerWhitelistCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _customerRepository = customerRepository;
    _uow = uow;
  }

  public override async Task Handle(SetCustomerWhitelistCommand request, CancellationToken ct)
  {
    var customer = await _customerRepository.GetByIdAsync(request.CustomerId)
        ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

    if (customer.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    if (request.IsWhitelisted)
    {
      customer.Whitelist();
    }
    else
    {
      customer.Unwhitelist();
    }

    await _uow.SaveChangesAsync(ct);
  }
}
