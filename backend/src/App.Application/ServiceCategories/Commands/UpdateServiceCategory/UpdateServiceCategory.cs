using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using App.Application.Common.Interfaces;
using App.Application.Common;
using MediatR;

namespace App.Application.ServiceCategories.Commands.UpdateServiceCategory;

public record UpdateServiceCategoryCommand(Guid Id, string Name, int OrderIndex) : IRequest;

internal class UpdateServiceCategoryHandler : TenantHandler<UpdateServiceCategoryCommand>
{
  private readonly IServiceCategoryRepository _repository;
  private readonly IUnitOfWork _uow;

  public UpdateServiceCategoryHandler(IServiceCategoryRepository repository, IUnitOfWork uow, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(UpdateServiceCategoryCommand request, CancellationToken ct)
  {
    var category = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(ServiceCategory), request.Id);

    if (category.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    category.Update(request.Name, request.OrderIndex);

    _repository.Update(category);
    await _uow.SaveChangesAsync(ct);
  }
}