namespace App.Api.Authentication;

/// <summary>Nagłówki i schemat uwierzytelniania używane tylko w <c>ASPNETCORE_ENVIRONMENT=Testing</c> (testy integracyjne).</summary>
public static class IntegrationTestAuthHeaders
{
  public const string SchemeName = "IntegrationTest";

  /// <summary>Identity user id (GUID) zgodny z polem UserId pracownika w seedzie testów.</summary>
  public const string UserId = "X-Integration-Test-UserId";

  /// <summary>Role rozdzielone przecinkami, np. <c>Owner</c>, <c>Admin</c>.</summary>
  public const string Roles = "X-Integration-Test-Roles";
}
