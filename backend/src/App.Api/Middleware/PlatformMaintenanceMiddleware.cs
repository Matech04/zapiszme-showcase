using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Middleware;

/// <summary>
/// Globalny tryb serwisowy platformy (kill-switch admina). Gdy włączony, blokuje wszystkie operacje
/// zapisu (POST/PUT/PATCH/DELETE) dla wszystkich kont — ochrona danych na czas buga/ataku/migracji.
/// Uruchamiany PO <c>UseAuthentication</c> i <c>ImpersonationMiddleware</c>, PRZED <c>UseAuthorization</c>,
/// żeby znać role z <c>HttpContext.User</c>.
///
/// Przepuszczane mimo włączonego trybu:
/// <list type="bullet">
///   <item>odczyty (GET/HEAD/OPTIONS) — platforma pozostaje do przeglądania, chronimy tylko zapisy;</item>
///   <item>allowlista ścieżek (logowanie, sam przełącznik trybu, sesja wsparcia, health) — admin musi
///         móc się zalogować i wyłączyć tryb;</item>
///   <item>admin platformy (rola Admin) — może wszystko, by naprawić problem.</item>
/// </list>
/// Pozostałe zapisy → 503 + <c>Retry-After</c>.
/// </summary>
public sealed class PlatformMaintenanceMiddleware
{
  // Prefiksy ścieżek zawsze dozwolone (dopasowanie po segmentach, case-insensitive).
  private static readonly string[] AllowlistPrefixes =
  {
    "/api/auth",                     // logowanie / refresh / logout
    "/api/admin/system/maintenance", // sam przełącznik trybu serwisowego
    "/api/admin/impersonation",      // zarządzanie sesją wsparcia
    "/health",
  };

  private readonly RequestDelegate _next;

  public PlatformMaintenanceMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context, IPlatformMaintenanceState state)
  {
    var method = context.Request.Method;
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
    {
      await _next(context);
      return;
    }

    var snapshot = await state.GetAsync(context.RequestAborted);
    if (!snapshot.Enabled)
    {
      await _next(context);
      return;
    }

    var path = context.Request.Path;
    foreach (var prefix in AllowlistPrefixes)
    {
      if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
      {
        await _next(context);
        return;
      }
    }

    if (context.User?.IsInRole("Admin") == true)
    {
      await _next(context);
      return;
    }

    // Kształt zgodny z ProblemDetails z GlobalFallbackExceptionHandler; front (errorInterceptor)
    // mapuje po `errorCode` = "platform.maintenance".
    var problem = new ProblemDetails
    {
      Status = StatusCodes.Status503ServiceUnavailable,
      Title = "Przerwa serwisowa",
      Detail = string.IsNullOrWhiteSpace(snapshot.Message)
          ? "Trwają prace serwisowe platformy — zapisywanie zmian jest tymczasowo wstrzymane. Spróbuj ponownie za chwilę."
          : snapshot.Message,
      Instance = context.Request.Path,
    };
    problem.Extensions["errorCode"] = "platform.maintenance";
    problem.Extensions["messageKey"] = "platform.maintenance";

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.Headers.Append("Retry-After", "120");
    await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
  }
}
