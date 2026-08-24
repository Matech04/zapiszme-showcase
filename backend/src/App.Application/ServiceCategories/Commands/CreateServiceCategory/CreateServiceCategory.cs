using App.Domain.Aggregates.ServiceAggregate;
using App.Application.Common.Interfaces;
using App.Application.Common;
using MediatR;

namespace App.Application.ServiceCategories.Commands.CreateServiceCategory;

public record CreateServiceCategoryCommand(string Name, int OrderIndex) : IRequest<Guid>;

internal class CreateServiceCategoryHandler : TenantHandler<CreateServiceCategoryCommand, Guid>
{
  private readonly IServiceCategoryRepository _repository;
  private readonly IUnitOfWork _uow;

  public CreateServiceCategoryHandler(IServiceCategoryRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(CreateServiceCategoryCommand request, CancellationToken ct)
  {
    var serviceCategory = new ServiceCategory(TenantId, request.Name, request.OrderIndex);

    await _repository.AddAsync(serviceCategory);
    await _uow.SaveChangesAsync(ct);
    return serviceCategory.Id;
  }
}