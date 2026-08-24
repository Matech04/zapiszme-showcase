using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.ServiceCategories.Commands.ReorderServiceCategories;

/// <summary>
/// Batchowy zapis kolejności kategorii usług (drag&amp;drop) w jednej transakcji.
/// <see cref="OrderedCategoryIds"/> to docelowa kolejność — pozycja na liście staje się
/// <see cref="ServiceCategory.OrderIndex"/> (0-based). Nazwa kategorii zachowana
/// (Update(name, orderIndex)). Walidacja przynależności do tenanta przez globalny query filter
/// (READ) + sprawdzenie kompletności listy Id.
/// </summary>
public record ReorderServiceCategoriesCommand(List<Guid> OrderedCategoryIds) : IRequest;

internal class ReorderServiceCategoriesHandler : TenantHandler<ReorderServiceCategoriesCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public ReorderServiceCategoriesHandler(
      IApplicationDbContext context,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task Handle(ReorderServiceCategoriesCommand request, CancellationToken ct)
  {
    var ids = request.OrderedCategoryIds;
    if (ids.Count == 0)
    {
      return;
    }

    // Globalny query filter wycina inne tenanty oraz nieaktywne kategorie — cross-tenant
    // lub nieistniejące Id nie zwrócą się i odrzuci je walidacja kompletności poniżej.
    var categories = await _context.ServiceCategories
        .Where(c => ids.Contains(c.Id))
        .ToListAsync(ct);

    var found = categories.Select(c => c.Id).ToHashSet();
    var missing = ids.FirstOrDefault(id => !found.Contains(id));
    if (missing != Guid.Empty || found.Count != ids.Distinct().Count())
    {
      throw new NotFoundException(nameof(ServiceCategory), missing);
    }

    // Pozycja na liście = OrderIndex; nazwa zachowana. SaveChanges weryfikuje TenantViolation.
    for (var position = 0; position < ids.Count; position++)
    {
      var category = categories.First(c => c.Id == ids[position]);
      category.Update(category.Name, position);
    }

    await _uow.SaveChangesAsync(ct);
  }
}
