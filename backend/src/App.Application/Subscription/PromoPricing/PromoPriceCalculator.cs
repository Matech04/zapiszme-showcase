using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using DomainSubscription = App.Domain.Aggregates.TenantAggregate.Subscription;

namespace App.Application.Subscription.PromoPricing;

/// <summary>
/// Liczenie końcowej miesięcznej ceny salonu z uwzględnieniem ewentualnego rabatu z
/// aktywnego <see cref="PromoCodeRedemption"/>. Domena nie zna redemption, dlatego pricing
/// żyje w warstwie aplikacji — handler ładuje sub + redemption i woła ten kalkulator.
/// </summary>
public static class PromoPriceCalculator
{
  /// <summary>Cena bazowa (per-seat, z uwzględnieniem Founding Member) bez aplikowanego rabatu.</summary>
  public static int CalculateBasePrice(DomainSubscription subscription) =>
    subscription.MonthlyPriceInGrosze;

  /// <summary>
  /// Cena ostateczna z aplikowanym rabatem. Jeśli redemption brakuje, nieaktywny, lub wygasł —
  /// zwraca cenę bazową. PriceOverride zastępuje tylko bazę (seats nadal +35 zł). PercentOff
  /// zniża cały rachunek. FreeMonths / TrialExtension nie wpływają na cenę.
  /// </summary>
  public static int CalculateFinalPrice(
    DomainSubscription subscription,
    PromoCodeRedemption? redemption,
    DateTime now)
  {
    var basePrice = CalculateBasePrice(subscription);

    if (redemption is null) return basePrice;
    if (!redemption.IsActive) return basePrice;
    if (redemption.ExpiresAt is { } end && now.ToUniversalTime() > end) return basePrice;

    var snapshot = DiscountSnapshot.TryParse(redemption.DiscountSnapshot);
    if (snapshot is null) return basePrice;

    return snapshot.Type switch
    {
      PromoDiscountType.PriceOverride =>
        // (snapshot.value zł * 100) + 35 zł * (Seats - 1) — PriceOverride wpływa tylko na bazę.
        (int)Math.Round(snapshot.Value * 100m)
        + DomainSubscription.AdditionalSeatPriceGrosze * (subscription.Seats - 1),

      PromoDiscountType.PercentOff =>
        (int)Math.Round(basePrice * (1m - snapshot.Value / 100m)),

      // FreeMonths / TrialExtension nie wpływają na cenę miesięczną.
      _ => basePrice,
    };
  }
}
