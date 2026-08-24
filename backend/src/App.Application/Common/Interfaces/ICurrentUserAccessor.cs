namespace App.Application.Common.Interfaces;

/// <summary>Kontekst wywołującego z Identity + middleware tenanta.</summary>
public interface ICurrentUserAccessor
{
  /// <summary>Identity user id zalogowanego użytkownika (null, gdy nieuwierzytelniony).</summary>
  Guid? UserId { get; }

  /// <summary>Id encji pracownika powiązanej z bieżącym użytkownikiem Identity w tym tenantcie.</summary>
  Guid? CallerEmployeeId { get; }

  /// <summary>Owner lub Manager (lub platformowy Admin) — może wykonywać operacje w imieniu innych pracowników.</summary>
  bool CanManageOtherEmployees { get; }

  /// <summary>Platformowy administrator (rola Admin) — uprawniony do trybu support impersonation.</summary>
  bool IsSystemAdmin { get; }

  /// <summary>
  /// Konto „Recepcja" (rola Kiosk) — współdzielona konsola salonu bez własnego kalendarza.
  /// Jedyna rola, która widzi powiadomienia o wizytach WSZYSTKICH pracowników; pozostałe
  /// (Owner/Manager/Employee) mają dzwonek osobisty.
  /// </summary>
  bool IsDeskAccount { get; }

  /// <summary>
  /// Aktywna sesja wsparcia (Admin w trybie impersonacji salonu). Do diagnostyki traktujemy ją
  /// przy powiadomieniach jak <see cref="IsDeskAccount"/> — widok całego salonu, a nie osobisty
  /// (admin nie ma rekordu Employee, więc dzwonek osobisty byłby pusty). Zakres i tak ogranicza
  /// query filter do impersonowanego tenanta. Domyślnie false — nadpisuje tylko realny accessor.
  /// </summary>
  bool IsSupportSession => false;
}
