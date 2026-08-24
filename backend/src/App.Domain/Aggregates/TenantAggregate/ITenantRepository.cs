using App.Domain.Aggregates.TenantAggregate;

public interface ITenantRepository
{
  Task AddAsync(Tenant tenant);
  Task<Tenant?> GetByIdAsync(Guid id);
  void Update(Tenant tenant);
  void Remove(Tenant tenant);
}