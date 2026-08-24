namespace App.Application.Common.Interfaces;

/// <summary>
/// Rozwiązuje tenant_id po Identity user id — bez użycia ApplicationDbContext,
/// żeby uniknąć cyklu DI z globalnym filtrem tenanta.
/// </summary>
public interface IEmployeeTenantResolver
{
  Task<Guid?> GetTenantIdByUserIdAsync(Guid userId, CancellationToken ct = default);
}
