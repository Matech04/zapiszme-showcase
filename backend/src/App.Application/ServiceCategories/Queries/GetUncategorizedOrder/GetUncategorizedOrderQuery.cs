using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.ServiceCategories.Queries.GetUncategorizedOrder;

/// <summary>
/// Lekki odczyt pozycji wirtualnej sekcji „Bez kategorii" w katalogu (do unified-reorder w UI).
/// Dashboard używa go, by wiedzieć, gdzie wstawić sekcję między realnymi kategoriami.
/// </summary>
public record GetUncategorizedOrderQuery : IRequest<UncategorizedOrderDto>;

/// <summary>Pozycja sekcji „Bez kategorii" (0-based; duży default = sekcja na końcu).</summary>
public record UncategorizedOrderDto(int OrderIndex);

internal class GetUncategorizedOrderHandler : TenantHandler<GetUncategorizedOrderQuery, UncategorizedOrderDto>
{
  private readonly ITenantRepository _tenantRepository;

  public GetUncategorizedOrderHandler(
      ITenantRepository tenantRepository,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _tenantRepository = tenantRepository;
  }

  public override async Task<UncategorizedOrderDto> Handle(GetUncategorizedOrderQuery request, CancellationToken ct)
  {
    var tenant = await _tenantRepository.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    return new UncategorizedOrderDto(tenant.UncategorizedOrderIndex);
  }
}
