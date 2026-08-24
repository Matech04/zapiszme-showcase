using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace App.Infrastructure.Middleware;

/// <summary>
/// Support impersonation: gdy uwierzytelniony administrator platformy ma ważne cookie sesji
/// wsparcia, nadpisuje rozwiązany tenant na tenant docelowy. Uruchamiany ZARAZ PO
/// <c>TenantIdentifierMiddleware</c> i PRZED <c>UseAuthorization</c>.
///
/// Wszystkie warunki muszą się zgodzić, inaczej żądanie idzie zwykłym torem (bez impersonacji):
/// poprawny podpis cookie, rola Admin, zgodność admina z sesją, sesja aktywna w bazie.
/// Tenant impersonacji NIE pochodzi z nagłówka kontrolowanego przez front — wyłącznie z
/// podpisanego cookie + rekordu w bazie (sprawdzanego co żądanie → natychmiastowa odwoływalność).
/// </summary>
public class ImpersonationMiddleware
{
  private static readonly HashSet<string> SafeMethods =
    new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

  /// <summary>
  /// Transport SignalR (<c>/hubs/*</c>) NIE jest mutacją, mimo że protokół używa POST-a:
  /// <c>/negotiate</c> to handshake, a przy long-pollingu POST-em idzie też ramka handshake'u.
  /// Blokowanie ich metodą HTTP wywracało połączenie dzwonka w każdej sesji wsparcia
  /// tylko-do-odczytu (403 <c>impersonation.read_only</c>, trzy błędy w konsoli, zejście na
  /// odpytywanie REST-em). Co gorsza, czyniło to martwym kodem gałąź w <c>NotificationHub</c>,
  /// która JAWNIE dopisuje sesję wsparcia do grupy salonu — funkcja była napisana, ale
  /// nieosiągalna.
  ///
  /// Wyjątek jest bezpieczny, bo hub jest JEDNOKIERUNKOWY: nie ma metod wołanych z klienta,
  /// więc przez to połączenie nie da się nic zapisać (<c>OnConnectedAsync</c> tylko czyta i
  /// dopisuje do grup). Tę przesłankę pilnuje test
  /// <c>ImpersonationHubTransportIntegrationTests.Huby_nie_wystawiaja_metod_wolanych_z_klienta</c> —
  /// gdy ktoś doda metodę klient→serwer, ten wyjątek trzeba przemyśleć od nowa.
  /// </summary>
  private static bool IsRealtimeHubTransport(PathString path) =>
    path.StartsWithSegments("/hubs");

  private readonly RequestDelegate _next;
  private readonly ILogger<ImpersonationMiddleware> _logger;

  public ImpersonationMiddleware(RequestDelegate next, ILogger<ImpersonationMiddleware> logger)
  {
    _next = next;
    _logger = logger;
  }

  public async Task InvokeAsync(
    HttpContext context,
    ApplicationDbContext dbContext,
    IImpersonationTicketService ticketService,
    TimeProvider timeProvider)
  {
    // Endpointy zarządzania sesją wsparcia (start/end/current/history) działają w normalnym
    // kontekście admina — nie nadpisujemy tenanta i nie blokujemy ich read-only (np. DELETE = koniec sesji).
    if (context.Request.Path.StartsWithSegments("/api/admin/impersonation"))
    {
      await _next(context);
      return;
    }

    // Publiczny, anonimowy panel rezerwacji (`/api/booking/{slug}/…`) ma tenant WYŁĄCZNIE ze slugu w
    // URL (TenantIdentifierMiddleware). Impersonacja NIE może go nadpisywać — inaczej dane
    // impersonowanego salonu (usługi/pracownicy/dostępność) wyciekają na publiczną stronę innego
    // salonu, gdy admin z aktywną sesją wsparcia otworzy cudzy kalendarz. Te endpointy są anonimowe
    // i nie należą do kontekstu wsparcia.
    if (context.Request.Path.StartsWithSegments("/api/booking"))
    {
      await _next(context);
      return;
    }

    if (!context.Request.Cookies.TryGetValue(ImpersonationConstants.CookieName, out var token)
        || !ticketService.TryUnprotect(token, out var sessionId))
    {
      await _next(context);
      return;
    }

    // Cookie obecne i podpisane — od tej pory traktujemy jako próbę impersonacji.
    if (context.User.Identity?.IsAuthenticated != true || !context.User.IsInRole("Admin"))
    {
      _logger.LogWarning("Cookie impersonacji bez roli Admin — ignoruję. SessionId {SessionId}", sessionId);
      await _next(context);
      return;
    }

    var callerUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    var session = await dbContext.ImpersonationSessions
      .AsNoTracking()
      .FirstOrDefaultAsync(s => s.Id == sessionId);

    var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

    if (session is null || !session.IsActive(nowUtc))
    {
      _logger.LogInformation("Sesja wsparcia nieaktywna/wygasła — ignoruję. SessionId {SessionId}", sessionId);
      await _next(context);
      return;
    }

    // Cookie musi należeć do tego samego admina, który uruchomił sesję.
    if (!Guid.TryParse(callerUserId, out var callerGuid) || callerGuid != session.AdminUserId)
    {
      _logger.LogWarning(
        "Cookie impersonacji nie pasuje do admina sesji. SessionId {SessionId}, Caller {Caller}",
        sessionId, callerUserId);
      await _next(context);
      return;
    }

    // Tryb tylko-do-odczytu: blokujemy mutacje. Wyjątek łapie GlobalFallbackExceptionHandler → 403.
    // Transport realtime jest wyłączony spod tej reguły — patrz IsRealtimeHubTransport.
    if (session.IsReadOnly
        && !SafeMethods.Contains(context.Request.Method)
        && !IsRealtimeHubTransport(context.Request.Path))
    {
      throw new ForbiddenAccessException(
        "Sesja wsparcia działa w trybie tylko do odczytu — operacja zapisu jest zablokowana.",
        ErrorCodes.ImpersonationReadOnly);
    }

    // Nadpisanie kontekstu tenanta na tenant docelowy.
    context.Items[TenantResolutionHttpContextKeys.ResolvedTenantId] = session.TargetTenantId;
    // Admin nie jest pracownikiem tego tenanta — czyścimy ewentualne dopasowanie z TenantIdentifierMiddleware.
    context.Items.Remove(TenantResolutionHttpContextKeys.ResolvedCallerEmployeeId);

    // Dla audytu (enricher logów).
    context.Items[ImpersonationHttpContextKeys.SessionId] = session.Id;
    context.Items[ImpersonationHttpContextKeys.AdminUserId] = session.AdminUserId;

    using (_logger.BeginScope(new Dictionary<string, object?>
    {
      ["ImpersonationSessionId"] = session.Id,
      ["ImpersonationAdminUserId"] = session.AdminUserId,
      ["TenantId"] = session.TargetTenantId,
    }))
    {
      // Debug, nie Information: ta sama para (ImpersonationSessionId, AdminUserId) jest doklejana
      // przez EnrichDiagnosticContext do linii „HTTP … responded" KAŻDEGO żądania, a sama sesja ma
      // wiersz w tabeli ImpersonationSession. Na Information dublowało to log przy każdym żądaniu.
      _logger.LogDebug(
        "Support impersonation aktywna: admin {AdminUserId} → tenant {TenantId} (read-only={ReadOnly})",
        session.AdminUserId, session.TargetTenantId, session.IsReadOnly);

      await _next(context);
    }
  }
}
