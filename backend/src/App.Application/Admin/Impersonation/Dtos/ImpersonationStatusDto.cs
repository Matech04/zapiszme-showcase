namespace App.Application.Admin.Impersonation.Dtos;

/// <summary>Stan aktywnej sesji wsparcia — dla banera w dashboardzie.</summary>
public sealed record ImpersonationStatusDto(
  Guid SessionId,
  Guid TenantId,
  string TenantName,
  string TenantSlug,
  string Reason,
  bool IsReadOnly,
  DateTime ExpiresAtUtc,
  int RemainingSeconds);
