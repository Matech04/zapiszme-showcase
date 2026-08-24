namespace App.Application.Common;

public static class TenantResolutionHttpContextKeys
{
  public const string ResolvedTenantId = "__ResolvedTenantId";

  /// <summary>Id rekordu pracownika dopasowanego do Identity user id (TenantIdentifierMiddleware).</summary>
  public const string ResolvedCallerEmployeeId = "__ResolvedCallerEmployeeId";
}
