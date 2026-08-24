using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class ShiftTemplateRepository : IShiftTemplateRepository
{
  private readonly IApplicationDbContext _context;

  public ShiftTemplateRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(ShiftTemplate template) => await _context.ShiftTemplates.AddAsync(template);

  public Task<ShiftTemplate?> GetByIdAsync(Guid id) =>
    _context.ShiftTemplates.FirstOrDefaultAsync(t => t.Id == id);

  public Task<List<ShiftTemplate>> GetByTenantAsync(Guid tenantId) =>
    _context.ShiftTemplates
      .AsNoTracking()
      .Where(t => t.TenantId == tenantId)
      .OrderBy(t => t.Name)
      .ToListAsync();

  public void Remove(ShiftTemplate template) => _context.ShiftTemplates.Remove(template);
}
