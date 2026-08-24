using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

public interface IAppointmentService
{
  // ignoreSchedule = true: personel zapisuje wizytę „poza grafikiem" — pomijamy okno pracy/urlop,
  // blokuje wyłącznie realna kolizja z inną (niecancelowaną) wizytą. Tylko ścieżka Panel.
  Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false);
  Task<bool> IsAvailableAsync(Employee employee, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false);
  // Wariant ignorujący wiele wizyt naraz — używany przy zamianie terminów.
  Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds);
  List<TimeOnly> EmployeeAvailableSlotsList(
    List<TimeRange> schedule,
    List<TimeRange> appointments,
    Employee employee,
    int totalDurationMinutes,
    int appointmentSlotStepMinutes);

  // Tryb stałych slotów: kandydatami są wpisane godziny (bez okna pracy ani siatki krokowej).
  List<TimeOnly> EmployeeFixedSlotsList(
    IReadOnlyList<TimeOnly> fixedStartTimes,
    List<TimeRange> appointments,
    Employee employee,
    int totalDurationMinutes);
}
