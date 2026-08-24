using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class EmployeeLeave : Entity
{
  public DateRange DateRange { get; private set; } = null!;
  public AbsenceType AbsenceType { get; private set; }
  public AbsenceStatus AbsenceStatus { get; private set; }

  public EmployeeLeave(
    DateRange dateRange,
    AbsenceType absenceType = AbsenceType.Vacation,
    AbsenceStatus absenceStatus = AbsenceStatus.Approved)
  {
    Id = Guid.NewGuid();
    DateRange = dateRange;
    AbsenceType = absenceType;
    AbsenceStatus = absenceStatus;
  }

  private EmployeeLeave() { }

  internal void UpdateDateRange(DateRange dateRange)
  {
    DateRange = dateRange;
  }

  internal void UpdateStatus(AbsenceStatus status)
  {
    AbsenceStatus = status;
  }
}

public enum AbsenceType
{
  Vacation = 1,
  SickLeave = 2,
  SpecialDay = 3
}

public enum AbsenceStatus
{
  Approved = 1,
  Pending = 2,
  Rejected = 3
}