namespace App.Domain.Aggregates.EmployeeAggregate;

public interface IShiftTemplateRepository
{
  Task AddAsync(ShiftTemplate template);
  Task<ShiftTemplate?> GetByIdAsync(Guid id);
  Task<List<ShiftTemplate>> GetByTenantAsync(Guid tenantId);
  void Remove(ShiftTemplate template);
}
