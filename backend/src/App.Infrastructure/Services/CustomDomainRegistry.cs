using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Services;

/// <summary>
/// Implementacja <see cref="ICustomDomainRegistry"/> trzymająca snapshot bazowych domen white-label
/// w pamięci. Odczyt jest lock-free (volatile + całościowa podmiana referencji HashSet). Snapshot
/// odświeża <c>CustomDomainRegistryRefresher</c> (BackgroundService) — patrz Program.cs.
/// </summary>
public sealed class CustomDomainRegistry : ICustomDomainRegistry
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<CustomDomainRegistry> _logger;

  private volatile HashSet<string> _baseDomains = new(StringComparer.OrdinalIgnoreCase);

  // Czy pierwszy udany refresh już się odbył. Pierwszy loguje się zawsze (potwierdzenie, że
  // rejestr wystartował), kolejne — tylko gdy zbiór faktycznie się zmienił.
  private volatile bool _loadedOnce;

  public CustomDomainRegistry(IServiceScopeFactory scopeFactory, ILogger<CustomDomainRegistry> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  public bool IsManagedHost(string? host)
  {
    if (string.IsNullOrWhiteSpace(host))
    {
      return false;
    }

    var normalized = host.Trim().ToLowerInvariant();

    // Serwujemy WYŁĄCZNIE rezerwacja.{d} (SPA) i api.{d} (API) — inne hosty odrzucamy od razu,
    // żeby Caddy nie wystawiał certów dla przypadkowych SNI wskazanych na nasz serwer.
    if (!normalized.StartsWith("rezerwacja.", StringComparison.Ordinal)
        && !normalized.StartsWith("api.", StringComparison.Ordinal))
    {
      return false;
    }

    var baseDomain = CustomDomainHost.ToBaseDomain(normalized);
    return baseDomain != null && _baseDomains.Contains(baseDomain);
  }

  public bool IsAllowedBookingOrigin(string? origin)
  {
    if (string.IsNullOrWhiteSpace(origin)
        || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || uri.Scheme != Uri.UriSchemeHttps)
    {
      return false;
    }

    // CORS dotyczy SPA bookingu serwowanego pod rezerwacja.{d}.
    if (!uri.Host.StartsWith("rezerwacja.", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var baseDomain = CustomDomainHost.ToBaseDomain(uri.Host);
    return baseDomain != null && _baseDomains.Contains(baseDomain);
  }

  public async Task RefreshAsync(CancellationToken cancellationToken = default)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var domains = await db.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(t => t.CustomDomain != null)
        .Select(t => t.CustomDomain!)
        .ToListAsync(cancellationToken);

    var previous = _baseDomains;
    var next = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
    _baseDomains = next;

    // Refresher chodzi co 5 minut, a zbiór zmienia się raz na wiele tygodni — bezwarunkowy
    // LogInformation dawał 288 linii na dobę, każda z tą samą liczbą. Wartość diagnostyczna jest
    // wyłącznie w MOMENCIE ZMIANY (doszła/zniknęła domena klienta), więc na Information idzie
    // pierwszy załadunek po starcie i każda realna różnica — reszta cykli na Debug.
    if (!_loadedOnce)
    {
      _loadedOnce = true;
      _logger.LogInformation("CustomDomainRegistry: załadowano {Count} white-label domen.", next.Count);
      return;
    }

    if (next.SetEquals(previous))
    {
      _logger.LogDebug("CustomDomainRegistry: bez zmian ({Count} white-label domen).", next.Count);
      return;
    }

    var added = next.Except(previous, StringComparer.OrdinalIgnoreCase).ToArray();
    var removed = previous.Except(next, StringComparer.OrdinalIgnoreCase).ToArray();

    _logger.LogInformation(
        "CustomDomainRegistry: zmiana zbioru white-label domen — {Count} łącznie, dodane: {Added}, usunięte: {Removed}.",
        next.Count,
        added,
        removed);
  }
}
