namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>Sposób wyliczenia domyślnej kwoty zadatku prefillowanej przy generowaniu linku.</summary>
public enum DepositMode
{
  /// <summary>Procent ceny usługi (<see cref="DepositSettings.Value"/> w zakresie 0–100).</summary>
  Percentage = 0,

  /// <summary>Stała kwota w walucie salonu (<see cref="DepositSettings.Value"/>).</summary>
  Fixed = 1,
}
