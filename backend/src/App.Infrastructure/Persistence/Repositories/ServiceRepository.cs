using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class ServiceRepository : IServiceRepository
{
  private readonly IApplicationDbContext _context;

  public ServiceRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(Service service) => await _context.Services.AddAsync(service);
  // FirstOrDefaultAsync (nie FindAsync) — globalny query filter tenanta (+IsActive) jako backstop izolacji.
  public async Task<Service?> GetByIdAsync(Guid id) =>
    await _context.Services.FirstOrDefaultAsync(s => s.Id == id);
  public void Update(Service service) => _context.Services.Update(service);
  public void Remove(Service service) => _context.Services.Remove(service);
}