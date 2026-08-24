using App.Domain.Common;

namespace App.Domain.Aggregates.GlobalSettingsAggregate;

/// <summary>
/// Globalne ustawienia platformy — encja singleton (dokładnie jeden wiersz o <see cref="SingletonId"/>).
/// NIE implementuje <c>ITenantEntity</c> i NIE ma query filtra (świadomy wyjątek od reguły izolacji tenantów,
/// jak <c>PromoCode</c> / <c>ImpersonationSession</c>).
///
/// Nośnik globalnego trybu serwisowego (kill-switch platformy): gdy <see cref="MaintenanceEnabled"/>
/// = true, <c>PlatformMaintenanceMiddleware</c> blokuje wszystkie operacje zapisu (POST/PUT/PATCH/DELETE)
/// dla wszystkich kont z wyjątkiem admina platformy — ochrona danych na czas buga/ataku/migracji.
/// </summary>
public class GlobalSettings : Entity, IAggregateRoot
{
  /// <summary>Stały identyfikator jedynego wiersza ustawień globalnych.</summary>
  public static readonly Guid SingletonId = new("0f9b6d5e-0000-4000-8000-000000000001");

  /// <summary>Globalny tryb serwisowy platformy. Domyślnie <c>false</c>.</summary>
  public bool MaintenanceEnabled { get; private set; }

  /// <summary>Opcjonalny komunikat pokazywany klientom na publicznej stronie rezerwacji podczas prac.</summary>
  public string? MaintenanceMessage { get; private set; }

  /// <summary>Moment (UTC) pierwszego włączenia bieżącej sesji trybu serwisowego. <c>null</c> gdy wyłączony.</summary>
  public DateTime? MaintenanceStartedAtUtc { get; private set; }

  /// <summary>Maksymalna długość komunikatu trybu serwisowego.</summary>
  public const int MaintenanceMessageMaxLength = 280;

  private GlobalSettings() { }

  /// <summary>Tworzy domyślny (wyłączony) wiersz singletona.</summary>
  public static GlobalSettings CreateDefault() => new() { Id = SingletonId };

  /// <summary>
  /// Włącza/wyłącza globalny tryb serwisowy i ustawia opcjonalny komunikat. Pusty/whitespace komunikat
  /// → <c>null</c> (domyślny tekst). Przy wyłączeniu komunikat i znacznik czasu są czyszczone. Komunikat
  /// jest defensywnie przycinany do <see cref="MaintenanceMessageMaxLength"/> (twarda walidacja w walidatorze).
  /// </summary>
  public void SetMaintenance(bool enabled, string? message, DateTime nowUtc)
  {
    if (!enabled)
    {
      MaintenanceEnabled = false;
      MaintenanceMessage = null;
      MaintenanceStartedAtUtc = null;
      return;
    }

    // Zachowaj czas pierwszego włączenia sesji (kolejne edycje komunikatu go nie resetują).
    MaintenanceStartedAtUtc ??= nowUtc;
    MaintenanceEnabled = true;

    var trimmed = message?.Trim();
    MaintenanceMessage = string.IsNullOrEmpty(trimmed)
        ? null
        : trimmed.Length > MaintenanceMessageMaxLength ? trimmed[..MaintenanceMessageMaxLength] : trimmed;
  }
}
