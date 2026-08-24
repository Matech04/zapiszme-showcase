using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;

namespace App.Application.Tenants.Commands.CreateTenant;

public record CreateTenantCommand(string Name, string Slug, string TimeZoneId, string Currency) : IRequest<Guid>;

internal class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Guid>
{
  private readonly ITenantRepository _repository;
  private readonly IUnitOfWork _uow;


  public CreateTenantCommandHandler(ITenantRepository repository, IUnitOfWork uow)
  {
    _repository = repository;
    _uow = uow;
  }

  public async Task<Guid> Handle(CreateTenantCommand request, CancellationToken ct)
  {
    var tenant = new Tenant(request.Name, request.Slug, request.TimeZoneId, request.Currency);
    // Bezpośrednie utworzenie tenanta (poza kreatorem) traktujemy jako w pełni skonfigurowane —
    // guard /admin/** nie może kierować takiego właściciela do /setup.
    tenant.MarkOnboardingCompleted(DateTime.UtcNow);

    await _repository.AddAsync(tenant);
    await _uow.SaveChangesAsync(ct);

    return tenant.Id;
  }
}
