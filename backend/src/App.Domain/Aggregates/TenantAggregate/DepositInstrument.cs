namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Instrument prawny pobierany od klienta przy rezerwacji — etykieta pokazywana w Checkout oraz
/// w panelu, o różnych skutkach prawnych (art. 394 KC). Umowa jest między klientem a salonem;
/// platforma dostarcza wyłącznie etykietę i mechanizm płatności.
/// </summary>
public enum DepositInstrument
{
  /// <summary>Zadatek (art. 394 KC) — przy niewykonaniu przez klienta salon zatrzymuje kwotę.</summary>
  Zadatek = 0,

  /// <summary>Zaliczka — zawsze zwrotna 1:1, nie zabezpiecza przed no-show.</summary>
  Zaliczka = 1,

  /// <summary>Opłata rezerwacyjna — łagodniejsza nazwa, skutki zależne od regulaminu salonu.</summary>
  OplataRezerwacyjna = 2,
}
