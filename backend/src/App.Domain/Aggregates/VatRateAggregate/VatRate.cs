using System.Text.RegularExpressions;
using App.Domain.Common;

public class VatRate : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }
  public string Name { get; private set; } = string.Empty;
  public decimal Value { get; private set; }
  public bool IsDefault { get; private set; } = false;
  public bool IsActive { get; private set; } = true;

  public VatRate(Guid tenant_id, string name, decimal value, bool isDefault = false)
  {
    Id = Guid.NewGuid();
    TenantId = tenant_id;
    Update(name, value, isDefault);
  }

  private VatRate() { }

  public void Update(string name, decimal value, bool isDefault)
  {
    Guard.AgainstInvalidRange(value, 0, 1, nameof(value));
    Guard.AgainstNegative(value, nameof(value));


    Name = Guard.NormalizeRequiredText(name, nameof(name));
    Value = value;
    IsDefault = isDefault;
  }

  public void Deactivate()
  {
    IsActive = false;
  }

  public void SetDefault()
  {
    IsDefault = true;
  }

  public void UnDefault()
  {
    IsDefault = false;
  }

}