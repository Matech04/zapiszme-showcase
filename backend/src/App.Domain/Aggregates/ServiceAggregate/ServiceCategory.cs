using App.Domain.Common;

namespace App.Domain.Aggregates.ServiceAggregate;

public class ServiceCategory : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }
  public string Name { get; private set; } = string.Empty;
  public int OrderIndex { get; private set; }

  public bool IsActive { get; private set; } = true;

  public ServiceCategory(Guid tenantId, string name, int orderIndex)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    Update(name, orderIndex);
  }

  private ServiceCategory() { }

  public void Deactivate()
  {
    IsActive = false;
  }

  public void Update(string name, int orderIndex)
  {

    Name = Guard.NormalizeRequiredText(name, nameof(name));
    OrderIndex = orderIndex;
  }

}