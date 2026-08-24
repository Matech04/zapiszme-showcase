using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;

public class TenantRepository : ITenantRepository
{

  IApplicationDbContext _context;

  public TenantRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(Tenant tenant) => await _context.Tenants.AddAsync(tenant);
  public async Task<Tenant?> GetByIdAsync(Guid id) => await _context.Tenants.FindAsync(id);
  public void Update(Tenant tenant) => _context.Tenants.Update(tenant);
  public void Remove(Tenant tenant) => _context.Tenants.Remove(tenant);
}