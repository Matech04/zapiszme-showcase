using App.Domain.Aggregates.ServiceAggregate;
using App.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class ServiceCategoryRepository : IServiceCategoryRepository
{
  private readonly IApplicationDbContext _context;

  public ServiceCategoryRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(ServiceCategory serviceCategory) => await _context.ServiceCategories.AddAsync(serviceCategory);
  // FirstOrDefaultAsync (nie FindAsync) — globalny query filter tenanta (+IsActive) jako backstop izolacji.
  public async Task<ServiceCategory?> GetByIdAsync(Guid id) =>
    await _context.ServiceCategories.FirstOrDefaultAsync(sc => sc.Id == id);
  public void Update(ServiceCategory serviceCategory) => _context.ServiceCategories.Update(serviceCategory);
  public void Remove(ServiceCategory serviceCategory) => _context.ServiceCategories.Remove(serviceCategory);
}