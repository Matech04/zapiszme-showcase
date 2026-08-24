using App.Application.Subscription.PromoPricing;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using DomainSubscription = App.Domain.Aggregates.TenantAggregate.Subscription;

namespace App.Application.UnitTests.Subscription;

/// <summary>PROMO-PRICE-001..006 — kalkulator finalnej ceny z aplikowanym rabatem.</summary>
public sealed class PromoPriceCalculatorTests
{
  [Fact]
  public void FinalPrice_equals_base_when_no_redemption()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, null, DateTime.UtcNow);

    Assert.Equal(7900, final);
  }

  [Fact]
  public void FinalPrice_equals_base_when_redemption_expired()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);

    var snapshot = new DiscountSnapshot(PromoDiscountType.PriceOverride, 49m, null).Serialize();
    var redemption = new PromoCodeRedemption(
      tenantId: Guid.NewGuid(),
      promoCodeId: Guid.NewGuid(),
      redeemedAt: DateTime.UtcNow.AddDays(-30),
      expiresAt: DateTime.UtcNow.AddDays(-1),
      discountSnapshot: snapshot);

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    Assert.Equal(7900, final);
  }

  [Fact]
  public void FinalPrice_PriceOverride_replaces_base_keeps_seats_premium()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 3, foundingMember: false);

    var snapshot = new DiscountSnapshot(PromoDiscountType.PriceOverride, 49m, null).Serialize();
    var redemption = new PromoCodeRedemption(
      Guid.NewGuid(), Guid.NewGuid(),
      DateTime.UtcNow, DateTime.UtcNow.AddYears(1), snapshot);

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    // 49 zł baza + 2×35 zł seats = 119 zł = 11900 grosze
    Assert.Equal(11900, final);
  }

  [Fact]
  public void FinalPrice_PercentOff_applies_to_full_bill()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 3, foundingMember: false);

    var snapshot = new DiscountSnapshot(PromoDiscountType.PercentOff, 20m, 3).Serialize();
    var redemption = new PromoCodeRedemption(
      Guid.NewGuid(), Guid.NewGuid(),
      DateTime.UtcNow, DateTime.UtcNow.AddMonths(3), snapshot);

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    // (79 + 2×35) × 0.8 = 119.20 zł → grosze 11920
    Assert.Equal(11920, final);
  }

  [Fact]
  public void FinalPrice_FreeMonths_does_not_change_price()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 2, foundingMember: false);

    var snapshot = new DiscountSnapshot(PromoDiscountType.FreeMonths, 3m, null).Serialize();
    var redemption = new PromoCodeRedemption(
      Guid.NewGuid(), Guid.NewGuid(),
      DateTime.UtcNow, null, snapshot);

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    // 79 + 35 = 114 zł = 11400 grosze (bez wpływu FreeMonths)
    Assert.Equal(11400, final);
  }

  [Fact]
  public void FinalPrice_inactive_redemption_returns_base()
  {
    var sub = DomainSubscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);

    var snapshot = new DiscountSnapshot(PromoDiscountType.PriceOverride, 49m, null).Serialize();
    var redemption = new PromoCodeRedemption(
      Guid.NewGuid(), Guid.NewGuid(),
      DateTime.UtcNow, null, snapshot);
    redemption.Deactivate();

    var final = PromoPriceCalculator.CalculateFinalPrice(sub, redemption, DateTime.UtcNow);

    Assert.Equal(7900, final);
  }
}
