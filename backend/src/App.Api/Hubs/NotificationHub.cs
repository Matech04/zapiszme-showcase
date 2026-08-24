using System.Security.Claims;
using App.Application.Common;
using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Hubs;

/// <summary>
/// Hub SignalR powiadomień in-app. Dzwonek jest osobisty, więc połączenia grupujemy per
/// użytkownik (<c>user-{userId}</c>). Konto „Recepcja" (Kiosk) dodatkowo dołącza do grupy
/// <c>desk-{tenantId}</c>, bo obsługuje wizyty całego zespołu. <c>SignalRNotifier</c> pushuje
/// do grupy odbiorcy i do grupy recepcji. Klient-do-serwera nie ma metod — hub jest
/// jednokierunkowy (serwer → dashboard).
/// </summary>
[Authorize(Policy = "GeneralAccess")]
public sealed class NotificationHub : Hub
{
  private readonly IApplicationDbContext _context;
  private readonly ILogger<NotificationHub> _logger;

  public NotificationHub(IApplicationDbContext context, ILogger<NotificationHub> logger)
  {
    _context = context;
    _logger = logger;
  }

  /// <summary>Grupa osobista odbiorcy — wszystkie otwarte karty panelu jednego użytkownika.</summary>
  public static string UserGroupName(Guid userId) => $"user-{userId}";

  /// <summary>Grupa konta „Recepcja" salonu — widzi powiadomienia całego zespołu.</summary>
  public static string DeskGroupName(Guid tenantId) => $"desk-{tenantId}";

  public override async Task OnConnectedAsync()
  {
    var tenantId = await ResolveTenantIdAsync();
    if (tenantId is null)
    {
      _logger.LogWarning(
        "NotificationHub: połączenie {ConnectionId} bez rozwiązanego tenanta — zrywam.",
        Context.ConnectionId);
      Context.Abort();
      return;
    }

    var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
      _logger.LogWarning(
        "NotificationHub: połączenie {ConnectionId} bez user id — zrywam.",
        Context.ConnectionId);
      Context.Abort();
      return;
    }

    await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));

    // Recepcja nie ma własnego kalendarza — bez tej grupy jej dzwonek byłby pusty. Tryb wsparcia
    // (Admin w impersonacji) też dostaje grupę salonu, żeby na żywo widzieć powiadomienia zespołu.
    var isSupportSession =
      Context.GetHttpContext()?.Items.ContainsKey(ImpersonationHttpContextKeys.SessionId) == true;
    if (Context.User?.IsInRole("Kiosk") == true || isSupportSession)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, DeskGroupName(tenantId.Value));
    }

    await base.OnConnectedAsync();
  }

  /// <summary>
  /// Tenant z <c>HttpContext.Items</c> (ustawiony przez TenantIdentifierMiddleware na żądaniu
  /// negotiate). Fallback: bezpośredni lookup Employee po Identity user id — odporne na zmianę
  /// kolejności pipeline lub klienta ze skipNegotiation.
  /// </summary>
  private async Task<Guid?> ResolveTenantIdAsync()
  {
    var http = Context.GetHttpContext();
    if (http is not null
        && http.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedTenantId, out var v)
        && v is Guid resolvedId)
    {
      return resolvedId;
    }

    var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
      return null;
    }

    var employee = await _context.Employees
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive);

    return employee?.TenantId;
  }
}
