using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class EmployeeSchedule : Entity
{
  public DateRange ActiveRange { get; private set; } = null!;
  public int NumberOfCycles { get; private set; }

  /// <summary>
  /// Czy grafik obowiązuje. Nieaktywny grafik jest całkowicie ignorowany: nie generuje dostępności
  /// (pomijany w <see cref="Employee.ResolveScheduleDay"/>) i NIE bierze udziału w sprawdzaniu kolizji
  /// zakresów dat. Pozwala przygotować „szkic" nachodzący na obecny aktywny grafik i podmienić go później.
  /// </summary>
  public bool IsActive { get; private set; } = true;

  private readonly List<ScheduleDay> _scheduleDays = new();

  public IReadOnlyCollection<ScheduleDay> ScheduleDays => _scheduleDays.AsReadOnly();

  public EmployeeSchedule(DateRange activeRange, int numberOfCycles, IEnumerable<ScheduleDay> scheduleDays, bool isActive = true)
  {
    Id = Guid.NewGuid();
    Guard.AgainstInvalidRange(numberOfCycles, 1, 4, nameof(numberOfCycles));
    ActiveRange = activeRange;
    NumberOfCycles = numberOfCycles;
    IsActive = isActive;
    SetEmployeeSchedule(scheduleDays);
  }

  private EmployeeSchedule() { }

  internal void SetActive(bool isActive)
  {
    IsActive = isActive;
  }

  internal void SetEmployeeSchedule(IEnumerable<ScheduleDay> scheduleDays)
  {
    var days = scheduleDays.ToList();
    var maxDays = NumberOfCycles * 7;

    if (days.Count > maxDays)
    {
      throw new InvalidScheduleDaysCountException();
    }

    if (days.Any(d => d.CycleIndex < 0 || d.CycleIndex >= maxDays))
    {
      throw new InvalidCycleIndexException();
    }

    if (days.GroupBy(d => d.CycleIndex).Any(g => g.Count() > 1))
    {
      throw new ScheduleDaysCollisionException();
    }

    _scheduleDays.Clear();
    _scheduleDays.AddRange(days.OrderBy(d => d.CycleIndex));
  }

  internal void UpdateActiveRange(DateRange activeRange)
  {
    ActiveRange = activeRange;
  }

  internal void UpdateNumberOfCycles(int numberOfCycles)
  {
    Guard.AgainstInvalidRange(numberOfCycles, 1, 4, nameof(numberOfCycles));
    NumberOfCycles = numberOfCycles;
    SetEmployeeSchedule(_scheduleDays);
  }
}