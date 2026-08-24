namespace App.Application.Common.Interfaces;

/// <summary>
/// Rejestr white-label domen klientów (cache w pamięci). Źródło prawdy: <c>Tenant.CustomDomain</c>.
/// Snapshot odświeżany cyklicznie w tle; metody odczytu są synchroniczne i tanie (zero I/O) — używane
/// w gorących ścieżkach: polityka CORS (per request) oraz ask On-Demand TLS w Caddy (per handshake,
/// potencjalny flood nieznanych SNI → nie wolno tu dotykać bazy).
/// </summary>
public interface ICustomDomainRegistry
{
  /// <summary>
  /// Czy host (<c>rezerwacja.{d}</c> lub <c>api.{d}</c>) należy do zarejestrowanej white-label domeny.
  /// Dla ask-endpointu Caddy On-Demand TLS i pre-checku resolve-host.
  /// </summary>
  bool IsManagedHost(string? host);

  /// <summary>Czy origin (<c>https://rezerwacja.{d}</c>) jest dozwolony w CORS.</summary>
  bool IsAllowedBookingOrigin(string? origin);

  /// <summary>Przeładowuje snapshot z bazy (cyklicznie oraz po onboardingu/zmianie domeny tenanta).</summary>
  Task RefreshAsync(CancellationToken cancellationToken = default);
}
