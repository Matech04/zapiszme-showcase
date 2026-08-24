using App.Domain.Common;

namespace App.Application.Common.Interfaces;

public interface IDeletionService
{
  Task DeleteAsync<TEntity>(TEntity entity, CancellationToken ct = default)
      where TEntity : Entity, ISoftDelete, ITenantEntity;
}
