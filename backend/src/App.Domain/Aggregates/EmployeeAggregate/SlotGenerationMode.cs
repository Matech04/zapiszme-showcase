namespace App.Domain.Aggregates.EmployeeAggregate;

/// <summary>
/// Sposób wyznaczania slotów rezerwacji dla pracownika.
/// <list type="bullet">
/// <item><see cref="Grid"/> — siatka co N minut w obrębie godzin pracy (domyślny, jak Booksy).</item>
/// <item><see cref="FixedStartTimes"/> — pracownik wpisuje SAME godziny slotów; sloty są grafikiem,
/// bez przedziałów pracy i przerw. Dzień z pustą listą = wolny.</item>
/// </list>
/// </summary>
public enum SlotGenerationMode
{
  Grid = 0,
  FixedStartTimes = 1,
}
