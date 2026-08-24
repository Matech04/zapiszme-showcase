using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Common;

public class DeletionService : IDeletionService
{
  private readonly IApplicationDbContext _context;

  public DeletionService(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task DeleteAsync<TEntity>(TEntity entity, CancellationToken ct = default)
      where TEntity : Entity, ISoftDelete, ITenantEntity
  {
    // Zawsze używamy Soft Delete dla encji implementujących ISoftDelete.
    // Dzięki temu unikamy błędów Foreign Key w bazie danych dla relacji,
    // których nie sprawdzamy jawnie (np. Employee <-> EmployeeService).
    entity.Deactivate();
    await Task.CompletedTask;
  }
}
