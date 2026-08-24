using App.Domain.Common;

namespace App.Domain.Aggregates.AppointmentAggregate;

public interface IAppointmentRepository
{
  Task AddAsync(Appointment appointment);
  Task<Appointment?> GetByIdAsync(Guid id);
  void Update(Appointment appointment);
  void Remove(Appointment appointment);
  Task<bool> HasCollisionAsync(Guid employeeId, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null);
  Task<bool> HasCollisionAsync(Guid employeeId, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null);
  // Wariant ignorujący wiele wizyt naraz — potrzebny przy zamianie terminów, gdzie obie
  // przenoszone wizyty zwalniają swoje sloty i nie mogą kolidować ze sobą nawzajem.
  Task<bool> HasCollisionAsync(Guid employeeId, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds);
  Task<bool> HasCollisionInDateRangeAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid tenantId);
  Task<List<Appointment>> GetAppointmentsByDateAsync(Guid employeeId, DateOnly date, Guid tenantId);
  Task<List<Appointment>> GetAppointmentsInDateRangeAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid tenantId);
}
