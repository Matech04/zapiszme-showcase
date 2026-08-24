using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class ShiftTemplate : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }
  public string Name { get; private set; } = string.Empty;
  public ScheduleDay ScheduleDay { get; private set; } = null!;

  /// <summary>
  /// Tryb szablonu: <see cref="SlotGenerationMode.Grid"/> trzyma bloki pracy/przerwy,
  /// <see cref="SlotGenerationMode.FixedStartTimes"/> trzyma listę godzin startu w <see cref="ScheduleDay"/>.
  /// Szablon pasuje tylko do grafiku w tym samym trybie.
  /// </summary>
  public SlotGenerationMode SlotGenerationMode { get; private set; }

  public ShiftTemplate(Guid tenantId, string name, ScheduleDay scheduleDay, SlotGenerationMode slotGenerationMode)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    Name = Guard.NormalizeRequiredText(name, nameof(name));
    ScheduleDay = scheduleDay;
    SlotGenerationMode = slotGenerationMode;
  }

  private ShiftTemplate() { }

  public void Update(string name, ScheduleDay scheduleDay, SlotGenerationMode slotGenerationMode)
  {
    Name = Guard.NormalizeRequiredText(name, nameof(name));
    ScheduleDay = scheduleDay;
    SlotGenerationMode = slotGenerationMode;
  }
}
