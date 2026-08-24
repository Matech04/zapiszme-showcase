using App.Domain.Aggregates.ServiceAggregate;

namespace App.Domain.Aggregates.ServiceAggregate;

public interface IServiceRepository
{
  Task AddAsync(Service service);
  Task<Service?> GetByIdAsync(Guid id);
  void Update(Service service);
  void Remove(Service service);
}