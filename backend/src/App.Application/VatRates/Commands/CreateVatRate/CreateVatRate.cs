using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.VatRateAggregate;
using MediatR;

namespace App.Application.VatRates.Commands.CreateVatRate;

public record CreateVatRateCommand(string Name, decimal Value, bool IsDefault) : IRequest<Guid>;

internal class CreateVatRateHandler : TenantHandler<CreateVatRateCommand, Guid>
{
  private readonly IVatRateRepository _repository;
  private readonly IUnitOfWork _uow;

  public CreateVatRateHandler(IVatRateRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(CreateVatRateCommand request, CancellationToken ct)
  {
    var vatRate = new VatRate(TenantId, request.Name, request.Value, request.IsDefault);

    if (request.IsDefault)
    {
      await _repository.ClearDefaultForTenantExceptAsync(TenantId, vatRate.Id, ct);
    }

    await _repository.AddAsync(vatRate);
    await _uow.SaveChangesAsync(ct);

    return vatRate.Id;
  }
}