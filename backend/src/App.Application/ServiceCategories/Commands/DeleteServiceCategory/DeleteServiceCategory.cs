using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using App.Application.Common.Interfaces;
using App.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.ServiceCategories.Commands.DeleteServiceCategory;

public record DeleteServiceCategoryCommand(Guid Id) : IRequest;

internal class DeleteServiceCategoryHandler : TenantHandler<DeleteServiceCategoryCommand>
{
  private readonly IServiceCategoryRepository _repository;
  private readonly IApplicationDbContext _context;

  public DeleteServiceCategoryHandler(IServiceCategoryRepository repository, IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _context = context;
  }

  public override async Task Handle(DeleteServiceCategoryCommand request, CancellationToken ct)
  {
    var category = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(ServiceCategory), request.Id);

    if (category.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Odpinamy usługi tej kategorii do „Bez kategorii" — zostają AKTYWNE (orphany),
    // znikają tylko z grupy kategorii. Inaczej usługi „zawisają" pod nieaktywną
    // (odfiltrowaną) kategorią: liczą się do sumy, ale nie są widoczne w panelu,
    // a w publicznym bookingu nadal rezerwowalne (bug ghost-services).
    // Globalny query filter ogranicza zbiór do bieżącego tenanta i aktywnych usług.
    var services = await _context.Services
      .Where(s => s.CategoryId == request.Id)
      .ToListAsync(ct);

    foreach (var service in services)
    {
      service.DetachCategory();
    }

    category.Deactivate();
    _repository.Update(category);

    // Jedno SaveChanges = jedna jednostka pracy: odpięcie usług + deaktywacja kategorii
    // zapisują się atomowo. Write-side TenantViolation check w ApplicationDbContext
    // przechodzi — usługi mają niezmieniony (poprawny) TenantId.
    await _context.SaveChangesAsync(ct);
  }
}
