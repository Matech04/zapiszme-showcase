using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Tenants.Commands.UpdateTenant;

public record UpdateTenantRequest(string Name, string Slug, string TimeZoneId, string Currency);

public record UpdateTenantCommand(Guid Id, string Name, string Slug, string TimeZoneId, string Currency) : IRequest;

internal class UpdateTenantCommandHandler : IRequestHandler<UpdateTenantCommand>
{
  private readonly ITenantRepository _repository;
  private readonly IUnitOfWork _uow;


  public UpdateTenantCommandHandler(ITenantRepository repository, IUnitOfWork uow)
  {
    _repository = repository;
    _uow = uow;
  }

  public async Task Handle(UpdateTenantCommand request, CancellationToken ct)
  {
    var tenant = await _repository.GetByIdAsync(request.Id);

    if (tenant == null)
    {
      throw new NotFoundException(nameof(Tenant), request.Id);
    }

    tenant.Update(request.Name, request.Slug, null, null, request.TimeZoneId, request.Currency);

    _repository.Update(tenant);
    await _uow.SaveChangesAsync(ct);

  }
}
