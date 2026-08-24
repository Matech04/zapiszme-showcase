using App.Domain.Common;

namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Ustawienia zadatku salonu — owned type na <see cref="Tenant"/>. Steruje wyłącznie domyślną
/// (prefillowaną, edytowalną) kwotą i etykietą instrumentu. Generowanie linku jest ręczne
/// (personel klika „Generuj zadatek" na wizycie). Domyślnie wyłączone.
/// </summary>
public class DepositSettings
{
  public bool Enabled { get; private set; }
  public DepositMode Mode { get; private set; } = DepositMode.Percentage;

  /// <summary>Procent (0–100) gdy <see cref="Mode"/>=Percentage, albo kwota gdy Fixed.</summary>
  public decimal Value { get; private set; }

  public DepositInstrument Instrument { get; private set; } = DepositInstrument.Zadatek;

  private DepositSettings() { }

  public DepositSettings(bool enabled, DepositMode mode, decimal value, DepositInstrument instrument)
  {
    if (mode == DepositMode.Percentage)
    {
      Guard.AgainstInvalidRange(value, 0, 100, nameof(value));
    }
    else
    {
      Guard.AgainstNegative(value, nameof(value));
    }

    Enabled = enabled;
    Mode = mode;
    Value = value;
    Instrument = instrument;
  }

  public static DepositSettings Disabled() =>
    new(enabled: false, mode: DepositMode.Percentage, value: 0, instrument: DepositInstrument.Zadatek);

  /// <summary>
  /// Domyślna kwota zadatku do prefillu przy generowaniu linku, na podstawie ceny wizyty.
  /// Nie przekracza wartości wizyty (zadatek ≤ cena). Zaokrąglona do groszy.
  /// </summary>
  public Money ComputeDefault(Money appointmentTotal)
  {
    decimal raw = Mode == DepositMode.Percentage
      ? appointmentTotal.Amount * Value / 100m
      : Value;

    decimal capped = Math.Min(raw, appointmentTotal.Amount);
    decimal rounded = Math.Round(Math.Max(0, capped), 2, MidpointRounding.AwayFromZero);
    return new Money(rounded, appointmentTotal.Currency);
  }
}
