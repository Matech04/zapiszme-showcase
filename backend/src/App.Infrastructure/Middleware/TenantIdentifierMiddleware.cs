using App.Application.Common;
using App.Application.Common.Security;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

public class TenantIdentifierMiddleware
{
  // PERF-010: bez cache każdy publiczny request /api/booking/{slug}/* robi 1 zapytanie do Tenants.
  //
  // UWAGA: slug NIE jest niezmienny — właściciel zmienia go w ustawieniach salonu, a unikalny indeks
  // natychmiast zwalnia stary do przejęcia. Poprawność tego cache'u opiera się więc na jawnym
  // unieważnieniu w UpdateCurrentSalonSettings (ITenantSlugCache), nie na założeniu niezmienności.
  // Bez tego przez pełne TTL żądania na przejęty slug szłyby do POPRZEDNIEGO tenanta.
  private static readonly TimeSpan SlugCacheTtl = TimeSpan.FromMinutes(5);

  private readonly RequestDelegate _next;
  private readonly ILogger<TenantIdentifierMiddleware> _logger;
  private readonly IMemoryCache _cache;

  public TenantIdentifierMiddleware(RequestDelegate next, ILogger<TenantIdentifierMiddleware> logger, IMemoryCache cache)
  {
    _next = next;
    _logger = logger;
    _cache = cache;
  }

  public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
  {
    if (HttpMethods.IsOptions(context.Request.Method))
    {
      await _next(context);
      return;
    }

    var bookingSlug = TryGetBookingTenantSlug(context);

    // Publiczny panel rezerwacji: tenant wyłącznie ze slug w URL (nie z JWT — unikamy pomyłki z innym tenantem).
    if (bookingSlug != null)
    {
      if (await TryResolveFromSlug(context, dbContext, bookingSlug))
      {
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
          ["TenantId"] = GetResolvedTenantId(context),
          ["BookingSlug"] = bookingSlug,
        }))
        {
          await _next(context);
        }

        return;
      }

      _logger.LogWarning("Nieznany slug tenanta w ścieżce booking: {Slug}", bookingSlug);
      context.Response.StatusCode = StatusCodes.Status404NotFound;
      return;
    }

    if (context.User.Identity?.IsAuthenticated == true)
    {
      if (await TryResolveFromAuthenticatedUser(context, dbContext))
      {
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
          ["TenantId"] = GetResolvedTenantId(context),
          ["CallerEmployeeId"] = GetResolvedCallerEmployeeId(context),
          ["UserId"] = GetUserId(context),
        }))
        {
          await _next(context);
        }

        return;
      }
    }

    await _next(context);
  }

  private static string? TryGetBookingTenantSlug(HttpContext context)
  {
    var path = context.Request.Path.Value;
    if (string.IsNullOrEmpty(path))
    {
      return null;
    }

    var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 3)
    {
      return null;
    }

    if (!segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    if (!segments[1].Equals("booking", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var slug = segments[2];
    return string.IsNullOrWhiteSpace(slug) ? null : slug.Trim();
  }

  private async Task<bool> TryResolveFromAuthenticatedUser(HttpContext context, ApplicationDbContext dbContext)
  {
    var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);

    if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
    {
      return false;
    }

    // WYDAJNOŚĆ — projekcja na dwa skalary, NIE materializacja encji. To najgorętsza ścieżka
    // w całym API personelu: leci przy KAŻDYM uwierzytelnionym żądaniu. `Employee` to agregat
    // z czterema kolekcjami owned (Services, Leaves, Schedules→ScheduleDays→WorkRanges/Breaks,
    // Overrides→ScheduleDay→…), a EF ładuje kolekcje owned ZAWSZE z rodzicem — poprzednia wersja
    // robiła więc iloczyn kartezjański nad całym grafikiem pracownika po to, żeby odczytać
    // `TenantId` i `Id`. Potrzebne są dokładnie te dwa pola i nic więcej.
    var employee = await dbContext.Employees
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(e => e.UserId == userId && e.IsActive)
        .Select(e => new { e.Id, e.TenantId })
        .FirstOrDefaultAsync();

    if (employee != null)
    {
      _logger.LogDebug(
          "Tenant z Identity: UserId {UserId}, TenantId {TenantId}, EmployeeId {EmployeeId}",
          userId,
          employee.TenantId,
          employee.Id);

      context.Items[TenantResolutionHttpContextKeys.ResolvedTenantId] = employee.TenantId;
      context.Items[TenantResolutionHttpContextKeys.ResolvedCallerEmployeeId] = employee.Id;
      return true;
    }

    // SystemAdmin z definicji nie jest pracownikiem żadnego tenanta — tenant rozwiąże zaraz
    // ImpersonationMiddleware. To stan oczekiwany, więc nie zaśmiecamy WARN-ów (leciał on przy
    // KAŻDYM żądaniu sesji wsparcia). Dla konta personelu brak Employee to realna anomalia.
    if (context.User.IsInRole(Roles.Admin))
    {
      _logger.LogDebug("Admin {UserId} bez rekordu Employee — tenant rozwiąże impersonacja.", userId);
    }
    else
    {
      _logger.LogWarning("Zalogowany użytkownik o UserId {UserId} nie ma aktywnego rekordu Employee.", userId);
    }

    return false;
  }

  private async Task<bool> TryResolveFromSlug(HttpContext context, ApplicationDbContext dbContext, string slug)
  {
    // Klucz liczy TenantSlugCache — ten sam format po stronie zapisu i unieważniania.
    var cacheKey = App.Infrastructure.Middleware.TenantSlugCache.Key(slug);
    // Cache POZYTYWNY: tylko realnie znalezione tenanty. Unknown-slug nie cache'ujemy
    // (rzadkie w prod, a rejestracja nowego tenanta musi być widoczna od razu).
    if (_cache.TryGetValue(cacheKey, out object? cached) && cached is Guid cachedTenantId)
    {
      _logger.LogDebug("Tenant slug cache hit: {Slug} -> {TenantId}", slug, cachedTenantId);
      context.Items[TenantResolutionHttpContextKeys.ResolvedTenantId] = cachedTenantId;
      return true;
    }

    var tenant = await dbContext.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Slug == slug);

    if (tenant != null)
    {
      _logger.LogDebug("Tenant ze slug booking: {Slug} -> {TenantId}", slug, tenant.Id);
      _cache.Set(cacheKey, tenant.Id, SlugCacheTtl);
      context.Items[TenantResolutionHttpContextKeys.ResolvedTenantId] = tenant.Id;
      return true;
    }

    return false;
  }

  private static Guid? GetResolvedTenantId(HttpContext context)
  {
    return context.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedTenantId, out var value)
        && value is Guid id
        ? id
        : null;
  }

  private static Guid? GetResolvedCallerEmployeeId(HttpContext context)
  {
    return context.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedCallerEmployeeId, out var value)
        && value is Guid id
        ? id
        : null;
  }

  private static string? GetUserId(HttpContext context)
  {
    return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
  }
}
