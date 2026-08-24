using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Services.Commands.DeleteService;

public record DeleteServiceCommand(Guid Id) : IRequest;

internal class DeleteServiceHandler : TenantHandler<DeleteServiceCommand>
{
  private readonly IServiceRepository _repository;
  private readonly IDeletionService _deletionService;
  private readonly IUnitOfWork _uow;

  public DeleteServiceHandler(IServiceRepository repository, IDeletionService deletionService, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _deletionService = deletionService;
    _uow = uow;
  }

  public override async Task Handle(DeleteServiceCommand request, CancellationToken ct)
  {
    var service = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(Service), request.Id);

    if (service.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    await _deletionService.DeleteAsync(service, ct);
    await _uow.SaveChangesAsync(ct);
  }
}
