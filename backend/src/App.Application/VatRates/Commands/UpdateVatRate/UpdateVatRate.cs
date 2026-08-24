using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.VatRates.Commands.UpdateVatRate;

public record UpdateVatRateCommand(Guid Id, string Name, decimal Value, bool IsDefault) : IRequest;

internal class UpdateVatRateHandler : TenantHandler<UpdateVatRateCommand>
{
  private readonly IVatRateRepository _repository;
  private readonly IUnitOfWork _uow;

  public UpdateVatRateHandler(IVatRateRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(UpdateVatRateCommand request, CancellationToken ct)
  {
    var vatRate = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(VatRate), request.Id);

    if (vatRate.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    if (request.IsDefault)
    {
      await _repository.ClearDefaultForTenantExceptAsync(TenantId, request.Id, ct);
    }

    vatRate.Update(request.Name, request.Value, request.IsDefault);

    _repository.Update(vatRate);
    await _uow.SaveChangesAsync(ct);
  }
}