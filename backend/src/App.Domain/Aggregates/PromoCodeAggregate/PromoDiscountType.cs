namespace App.Domain.Aggregates.PromoCodeAggregate;

/// <summary>
/// Sposób działania rabatu.
/// <list type="bullet">
///   <item><c>PriceOverride</c> — zastępuje bazową cenę (np. 49 zł zamiast 79 zł). Wpływa tylko na bazę; seats nadal +35 zł.</item>
///   <item><c>PercentOff</c> — % zniżki od całego rachunku (baza + seats) przez <c>DurationMonths</c>.</item>
///   <item><c>FreeMonths</c> — wydłuża <c>CurrentPeriodEndsAt</c> o N miesięcy; cena nie zmieniona.</item>
///   <item><c>TrialExtension</c> — wydłuża <c>TrialEndsAt</c> o N dni (działa tylko podczas trialu); cena nie zmieniona.</item>
/// </list>
/// </summary>
public enum PromoDiscountType
{
  PriceOverride = 0,
  PercentOff = 1,
  FreeMonths = 2,
  TrialExtension = 3,
}
