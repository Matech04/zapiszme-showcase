using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Customers.Commands.UpdateCustomerNotes;

public record UpdateCustomerNotesCommand(
  Guid CustomerId,
  string newNotes) : IRequest<Guid>;

internal class UpdateCustomerNotesHandler : TenantHandler<UpdateCustomerNotesCommand, Guid>
{
  private readonly ICustomerRepository _repository;
  private readonly IUnitOfWork _uow;

  public UpdateCustomerNotesHandler(
    ICustomerRepository repository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(UpdateCustomerNotesCommand request, CancellationToken ct)
  {
    var customer = await _repository.GetByIdAsync(request.CustomerId)
        ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

    if (customer.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    customer.ChangeNotes(request.newNotes);

    _repository.Update(customer);
    await _uow.SaveChangesAsync(ct);

    return customer.Id;
  }
}
