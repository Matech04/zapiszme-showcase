
using System.Security.AccessControl;
using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class EmployeeService : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid EmployeeId { get; private set; }
  public Guid ServiceId { get; private set; }
  public int? CustomDuration { get; private set; }
  public Money? CustomPrice { get; private set; }

  internal EmployeeService(Guid tenantId, Guid employeeId, Guid serviceId, int? customDuration, Money? customPrice)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    EmployeeId = employeeId;
    ServiceId = serviceId;
    Update(customDuration, customPrice);
  }

  private EmployeeService() { }

  /// <summary>
  /// Ustawia override per-pracownik. <c>null</c> = „dziedzicz z katalogu usługi" (Service.DurationInMinutes /
  /// Service.Price) — dzięki temu zmiana czasu/ceny usługi propaguje się automatycznie. Wartość niepusta
  /// to świadomy override (np. ten pracownik robi tę usługę szybciej / drożej).
  /// </summary>
  internal void Update(int? customDuration, Money? customPrice)
  {
    if (customDuration is { } duration)
    {
      Guard.AgainstNegative(duration, nameof(customDuration));
    }

    CustomDuration = customDuration;
    CustomPrice = customPrice;
  }
}