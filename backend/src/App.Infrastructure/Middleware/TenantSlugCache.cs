using App.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace App.Infrastructure.Middleware;

/// <summary>
/// Jedyne źródło formatu klucza cache'u slug→tenant. <c>TenantIdentifierMiddleware</c> zapisuje,
/// ten serwis unieważnia — obie strony muszą liczyć klucz tak samo, więc liczy go
/// <see cref="Key"/> i nikt inny.
///
/// Single-host <c>IMemoryCache</c>, spójnie z pozostałymi licznikami/throttle'ami w tym projekcie.
/// Przy skalowaniu do wielu instancji unieważnienie musi pójść na wspólny store (Redis pub/sub),
/// inaczej pozostałe instancje dalej trzymają stary wpis przez pełne TTL.
/// </summary>
public sealed class TenantSlugCache : ITenantSlugCache
{
  private readonly IMemoryCache _cache;

  public TenantSlugCache(IMemoryCache cache)
  {
    _cache = cache;
  }

  public static string Key(string slug) => $"tenant:slug:{slug}";

  public void Invalidate(string slug)
  {
    if (string.IsNullOrWhiteSpace(slug))
    {
      return;
    }

    _cache.Remove(Key(slug));
  }
}
