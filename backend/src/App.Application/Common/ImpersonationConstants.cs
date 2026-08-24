namespace App.Application.Common;

/// <summary>Stałe współdzielone przez warstwę API (cookie, controller) i Infrastructure (middleware).</summary>
public static class ImpersonationConstants
{
  /// <summary>Nazwa podpisanego cookie wskazującego aktywną sesję wsparcia.</summary>
  public const string CookieName = "impersonation";
}

/// <summary>Klucze HttpContext.Items ustawiane przez ImpersonationMiddleware (czytane m.in. przez enricher logów).</summary>
public static class ImpersonationHttpContextKeys
{
  public const string SessionId = "__ImpersonationSessionId";
  public const string AdminUserId = "__ImpersonationAdminUserId";
}
