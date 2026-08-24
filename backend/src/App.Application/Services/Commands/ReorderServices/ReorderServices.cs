using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Commands.ReorderServices;

/// <summary>
/// Batchowy zapis kolejności usług (drag&amp;drop) w jednej transakcji. <see cref="OrderedServiceIds"/>
/// to docelowa kolejność — pozycja na liście staje się <see cref="Service.OrderIndex"/> (0-based).
/// <see cref="CategoryId"/> jest informacyjne (UI sortuje w obrębie kategorii); walidacja przynależności
/// do tenanta odbywa się przez globalny query filter (READ) + sprawdzenie kompletności listy Id.
/// </summary>
public record ReorderServicesCommand(List<Guid> OrderedServiceIds, Guid? CategoryId) : IRequest;

internal class ReorderServicesHandler : TenantHandler<ReorderServicesCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public ReorderServicesHandler(
      IApplicationDbContext context,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task Handle(ReorderServicesCommand request, CancellationToken ct)
  {
    var ids = request.OrderedServiceIds;
    if (ids.Count == 0)
    {
      return;
    }

    // Pobieramy usługi po Id w obrębie tenanta. Globalny query filter wycina inne tenanty
    // oraz nieaktywne usługi — dlatego cross-tenant lub nieistniejące Id NIE zwrócą się tutaj
    // i poniższa walidacja kompletności je odrzuci (NotFound).
    var services = await _context.Services
        .Where(s => ids.Contains(s.Id))
        .ToListAsync(ct);

    var found = services.Select(s => s.Id).ToHashSet();
    var missing = ids.FirstOrDefault(id => !found.Contains(id));
    if (missing != Guid.Empty || found.Count != ids.Distinct().Count())
    {
      // Któreś Id nie należy do tenanta (lub nie istnieje / jest nieaktywne) — odrzucamy całą operację.
      throw new NotFoundException(nameof(Service), missing);
    }

    // Pozycja na liście = OrderIndex. SaveChanges weryfikuje TenantViolation po stronie zapisu.
    for (var position = 0; position < ids.Count; position++)
    {
      var service = services.First(s => s.Id == ids[position]);
      service.SetOrder(position);
    }

    await _uow.SaveChangesAsync(ct);
  }
}
