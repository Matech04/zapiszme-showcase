using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.VatRates.Commands.DeleteVatRate;

public record DeleteVatRateCommand(Guid Id) : IRequest;

internal class DeleteVatRateHandler : TenantHandler<DeleteVatRateCommand>
{
  private readonly IVatRateRepository _repository;
  private readonly IUnitOfWork _uow;

  public DeleteVatRateHandler(IVatRateRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(DeleteVatRateCommand request, CancellationToken ct)
  {
    var vatRate = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(VatRate), request.Id);

    if (vatRate.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    _repository.Remove(vatRate);
    await _uow.SaveChangesAsync(ct);
  }
}