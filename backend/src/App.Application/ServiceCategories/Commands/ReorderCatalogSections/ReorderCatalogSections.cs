using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.ServiceCategories.Commands.ReorderCatalogSections;

/// <summary>
/// Unified-reorder katalogu (drag&amp;drop): realne kategorie usług ORAZ wirtualna sekcja
/// „Bez kategorii" zapisane w JEDNEJ sekwencji w jednej transakcji.
/// <see cref="OrderedSections"/> to docelowa kolejność — pozycja na liście (0-based) staje się
/// <see cref="ServiceCategory.OrderIndex"/> dla kategorii albo
/// <see cref="Tenant.UncategorizedOrderIndex"/> dla sekcji „Bez kategorii".
/// Każdy element to: Guid kategorii ALBO <c>null</c> = sekcja „Bez kategorii". Co najwyżej JEDEN
/// <c>null</c> na liście. Wszystkie nie-null Id muszą należeć do tenanta (globalny query filter
/// wycina obce/nieaktywne — nieznane Id → <see cref="NotFoundException"/>).
/// </summary>
public record ReorderCatalogSectionsCommand(IReadOnlyList<Guid?> OrderedSections) : IRequest;

public class ReorderCatalogSectionsCommandValidator : AbstractValidator<ReorderCatalogSectionsCommand>
{
  public ReorderCatalogSectionsCommandValidator()
  {
    RuleFor(x => x.OrderedSections)
      .NotNull();

    // Co najwyżej jeden null (jedna sekcja „Bez kategorii").
    RuleFor(x => x.OrderedSections)
      .Must(sections => sections is null || sections.Count(s => s is null) <= 1)
      .WithMessage("Lista może zawierać co najwyżej jedną sekcję Bez kategorii (null).");

    // Brak duplikatów Id kategorii.
    RuleFor(x => x.OrderedSections)
      .Must(sections =>
      {
        if (sections is null)
        {
          return true;
        }

        var ids = sections.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        return ids.Count == ids.Distinct().Count();
      })
      .WithMessage("Lista nie może zawierać zduplikowanych identyfikatorów kategorii.");
  }
}

internal class ReorderCatalogSectionsHandler : TenantHandler<ReorderCatalogSectionsCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ITenantRepository _tenantRepository;
  private readonly IUnitOfWork _uow;

  public ReorderCatalogSectionsHandler(
      IApplicationDbContext context,
      ITenantRepository tenantRepository,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _tenantRepository = tenantRepository;
    _uow = uow;
  }

  public override async Task Handle(ReorderCatalogSectionsCommand request, CancellationToken ct)
  {
    var sections = request.OrderedSections;
    if (sections.Count == 0)
    {
      return;
    }

    var categoryIds = sections.Where(s => s.HasValue).Select(s => s!.Value).ToList();

    // Globalny query filter wycina inne tenanty oraz nieaktywne kategorie — cross-tenant
    // lub nieistniejące Id nie zwrócą się i odrzuci je sprawdzenie kompletności poniżej.
    var categories = categoryIds.Count == 0
      ? new List<ServiceCategory>()
      : await _context.ServiceCategories
          .Where(c => categoryIds.Contains(c.Id))
          .ToListAsync(ct);

    var found = categories.Select(c => c.Id).ToHashSet();
    var missing = categoryIds.FirstOrDefault(id => !found.Contains(id));
    if (missing != Guid.Empty)
    {
      throw new NotFoundException(nameof(ServiceCategory), missing);
    }

    Tenant? tenant = null;
    if (sections.Any(s => s is null))
    {
      tenant = await _tenantRepository.GetByIdAsync(TenantId)
        ?? throw new NotFoundException(nameof(Tenant), TenantId);
    }

    // Pozycja na liście = OrderIndex (kategoria) / UncategorizedOrderIndex (sekcja „Bez kategorii").
    // SaveChanges weryfikuje TenantViolation (write-side guard).
    for (var position = 0; position < sections.Count; position++)
    {
      var section = sections[position];
      if (section is null)
      {
        tenant!.SetUncategorizedOrder(position);
      }
      else
      {
        var category = categories.First(c => c.Id == section.Value);
        category.Update(category.Name, position);
      }
    }

    if (tenant is not null)
    {
      _tenantRepository.Update(tenant);
    }

    await _uow.SaveChangesAsync(ct);
  }
}
