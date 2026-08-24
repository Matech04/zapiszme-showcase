using App.Domain.Common;

namespace App.Domain.Aggregates.ServiceAggregate;

/// <summary>
/// Powiązanie „usługa główna → dozwolony dodatek" (M:N w obrębie tabeli Services, jeden tenant).
/// Należy do agregatu usługi głównej (<see cref="Service"/>) jako kolekcja owned. Dodatek to zwykła
/// usługa z flagą <see cref="Service.IsAddon"/> = true. Reguła „dodatek tylko z pasującą usługą główną"
/// egzekwowana jest w warstwie aplikacji (ComboCompositionValidator) na podstawie tych powiązań.
/// </summary>
public class ServiceAddon : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid MainServiceId { get; private set; }
  public Guid AddonServiceId { get; private set; }

  internal ServiceAddon(Guid tenantId, Guid mainServiceId, Guid addonServiceId)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    MainServiceId = mainServiceId;
    AddonServiceId = addonServiceId;
  }

  private ServiceAddon() { }
}
