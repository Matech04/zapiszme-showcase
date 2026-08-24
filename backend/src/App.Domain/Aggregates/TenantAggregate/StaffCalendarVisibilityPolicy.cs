namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Polityka widoczności kalendarza wizyt dla zwykłych pracowników (rola „Employee"). Owner /
/// Manager zawsze widzą wszystkich i mogą wszystko — ta polityka rozszerza uprawnienia roli
/// Employee.
/// </summary>
public enum StaffCalendarVisibilityPolicy
{
  /// <summary>Pracownik widzi tylko swój kalendarz; nie może oglądać ani edytować cudzych wizyt.</summary>
  OwnCalendarOnly = 1,

  /// <summary>Pracownik widzi cały zespół (read-only), edytować może tylko własne wizyty.</summary>
  TeamReadOnly = 2,

  /// <summary>Pracownik widzi cały zespół i może edytować/anulować/potwierdzać cudze wizyty.</summary>
  TeamFull = 3,
}
