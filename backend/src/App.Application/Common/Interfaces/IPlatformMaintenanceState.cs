namespace App.Application.Common.Interfaces;

/// <summary>Migawka globalnego trybu serwisowego platformy (źródło: <c>GlobalSettings</c>).</summary>
public sealed record PlatformMaintenanceSnapshot(bool Enabled, string? Message, DateTime? StartedAtUtc)
{
  public static readonly PlatformMaintenanceSnapshot Disabled = new(false, null, null);
}

/// <summary>
/// Szybki dostęp do stanu globalnego trybu serwisowego z krótkim cache (TTL), żeby middleware
/// nie odpytywał bazy przy każdym write-requeście. Inwalidowany natychmiast po przełączeniu trybu.
/// </summary>
public interface IPlatformMaintenanceState
{
  Task<PlatformMaintenanceSnapshot> GetAsync(CancellationToken ct);

  /// <summary>Natychmiastowa inwalidacja cache (po przełączeniu trybu przez admina platformy).</summary>
  void Invalidate();
}
