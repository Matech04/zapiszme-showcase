using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Customers.Commands.UpdateCustomer;

public record UpdateCustomerCommand(Guid Id, string FirstName, string LastName, string Email, string PhoneNumber, string GeneralNotes, string? InstagramNick = null) : IRequest;

internal class UpdateCustomerHandler : TenantHandler<UpdateCustomerCommand>
{
  private readonly ICustomerRepository _repository;
  private readonly IUnitOfWork _uow;

  public UpdateCustomerHandler(ICustomerRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(UpdateCustomerCommand request, CancellationToken ct)
  {
    var customer = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(Customer), request.Id);

    if (customer.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
        ? null
        : new PhoneNumber(request.PhoneNumber);

    customer.Update(
        request.FirstName,
        request.LastName,
        request.Email,
        phoneNumber,
        request.GeneralNotes,
        request.InstagramNick);

    _repository.Update(customer);
    await _uow.SaveChangesAsync(ct);
  }
}