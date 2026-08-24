namespace App.Application.Admin.Impersonation.Dtos;

/// <summary>Pozycja dziennika audytu sesji wsparcia.</summary>
public sealed record ImpersonationHistoryItemDto(
  Guid SessionId,
  Guid AdminUserId,
  Guid TargetTenantId,
  string TenantName,
  string Reason,
  bool IsReadOnly,
  DateTime StartedAtUtc,
  DateTime ExpiresAtUtc,
  DateTime? EndedAtUtc,
  string? IpAddress);
