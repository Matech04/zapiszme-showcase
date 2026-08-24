using App.Domain.Aggregates.ServiceAggregate;

public interface IServiceCategoryRepository
{
  Task AddAsync(ServiceCategory serviceCategory);
  Task<ServiceCategory?> GetByIdAsync(Guid id);
  void Update(ServiceCategory serviceCategory);
  void Remove(ServiceCategory serviceCategory);
}