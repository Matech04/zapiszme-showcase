using App.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Middleware;

/// <summary>
/// Cache stanu globalnego trybu serwisowego. Singleton — trzyma migawkę <c>GlobalSettings</c>
/// w <see cref="IMemoryCache"/> z krótkim TTL, żeby <c>PlatformMaintenanceMiddleware</c> nie
/// odpytywał bazy przy każdym write-requeście. Odczyt z bazy w osobnym scope (singleton nie może
/// trzymać scoped DbContext). Przełącznik admina woła <see cref="Invalidate"/> → natychmiastowy efekt.
/// </summary>
public sealed class PlatformMaintenanceState : IPlatformMaintenanceState
{
  private const string CacheKey = "platform:maintenance";
  private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

  private readonly IMemoryCache _cache;
  private readonly IServiceScopeFactory _scopeFactory;

  public PlatformMaintenanceState(IMemoryCache cache, IServiceScopeFactory scopeFactory)
  {
    _cache = cache;
    _scopeFactory = scopeFactory;
  }

  public async Task<PlatformMaintenanceSnapshot> GetAsync(CancellationToken ct)
  {
    if (_cache.TryGetValue(CacheKey, out PlatformMaintenanceSnapshot? cached) && cached is not null)
    {
      return cached;
    }

    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
    var settings = await db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(ct);

    var snapshot = settings is null
        ? PlatformMaintenanceSnapshot.Disabled
        : new PlatformMaintenanceSnapshot(settings.MaintenanceEnabled, settings.MaintenanceMessage, settings.MaintenanceStartedAtUtc);

    _cache.Set(CacheKey, snapshot, Ttl);
    return snapshot;
  }

  public void Invalidate() => _cache.Remove(CacheKey);
}
