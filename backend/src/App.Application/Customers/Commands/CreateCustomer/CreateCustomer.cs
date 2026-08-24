using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using MediatR;

namespace App.Application.Customers.Commands.CreateCustomer;

public record CreateCustomerCommand(string FirstName, string LastName, string Email, string PhoneNumber, string GeneralNotes, string? InstagramNick = null) : IRequest<Guid>;

internal class CreateCustomerHandler : TenantHandler<CreateCustomerCommand, Guid>
{
  private readonly ICustomerRepository _repository;
  private readonly IUnitOfWork _uow;

  public CreateCustomerHandler(ICustomerRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(CreateCustomerCommand request, CancellationToken ct)
  {
    var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
        ? null
        : new PhoneNumber(request.PhoneNumber);

    var customer = new Customer(
        TenantId,
        request.FirstName,
        request.LastName,
        request.Email,
        phoneNumber,
        request.GeneralNotes,
        request.InstagramNick);

    await _repository.AddAsync(customer);
    await _uow.SaveChangesAsync(ct);

    return customer.Id;
  }
}