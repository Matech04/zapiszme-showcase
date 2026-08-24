using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Api.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
  private readonly ApplicationDbContext _dbContext;

  public DatabaseHealthCheck(ApplicationDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<HealthCheckResult> CheckHealthAsync(
      HealthCheckContext context,
      CancellationToken cancellationToken = default)
  {
    try
    {
      var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
      if (!canConnect)
      {
        return HealthCheckResult.Unhealthy("Database connection is not available.");
      }

      if (_dbContext.Database.IsRelational())
      {
        var pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
          return HealthCheckResult.Unhealthy("Database has pending migrations.");
        }
      }

      return HealthCheckResult.Healthy("Database connection is available.");
    }
    catch (Exception ex)
    {
      return HealthCheckResult.Unhealthy("Database health check failed.", ex);
    }
  }
}
