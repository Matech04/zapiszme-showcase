using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

/// <summary>
/// PROMO-001..010 — factory methods PromoCode + CanBeRedeemed logika.
/// </summary>
public sealed class PromoCodeTests
{
  [Fact]
  public void NormalizeCode_uppercases_and_trims()
  {
    Assert.Equal("FOUNDING10", PromoCode.NormalizeCode(" founding10 "));
    Assert.Equal("ABC", PromoCode.NormalizeCode("aBc"));
  }

  [Fact]
  public void CreatePriceOverride_persists_value()
  {
    var code = PromoCode.CreatePriceOverride("FOUNDING10", 49.0m, maxTotalUses: 10);
    Assert.Equal("FOUNDING10", code.Code);
    Assert.Equal(PromoDiscountType.PriceOverride, code.DiscountType);
    Assert.Equal(49.0m, code.DiscountValue);
    Assert.Equal(10, code.MaxTotalUses);
    Assert.True(code.IsActive);
  }

  [Fact]
  public void CreatePriceOverride_rejects_negative_price()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      PromoCode.CreatePriceOverride("X", -1m));
  }

  [Fact]
  public void CreatePercentOff_requires_percent_in_range()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => PromoCode.CreatePercentOff("X", 0m, 3));
    Assert.Throws<ArgumentOutOfRangeException>(() => PromoCode.CreatePercentOff("X", 150m, 3));
  }

  [Fact]
  public void CreatePercentOff_requires_positive_duration()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => PromoCode.CreatePercentOff("X", 20m, 0));
  }

  [Fact]
  public void CreateTrialExtension_forces_NewTenantsOnly_applies_to()
  {
    var code = PromoCode.CreateTrialExtension("EXTEND30", days: 30);
    Assert.Equal(PromoCodeAppliesTo.NewTenantsOnly, code.AppliesTo);
    Assert.Equal(30m, code.DiscountValue);
  }

  [Fact]
  public void CanBeRedeemed_rejects_when_inactive()
  {
    var code = PromoCode.CreatePriceOverride("INACTIVE", 49m);
    code.Deactivate();

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, true);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_rejects_when_past_validUntil()
  {
    var code = PromoCode.CreatePriceOverride("EXPIRED", 49m,
      validFrom: DateTime.UtcNow.AddDays(-10),
      validUntil: DateTime.UtcNow.AddDays(-1));

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, true);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_rejects_when_MaxTotalUses_reached()
  {
    var code = PromoCode.CreatePriceOverride("CAPPED", 49m, maxTotalUses: 1);
    code.IncrementUses();

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, true);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void DecrementUses_releases_one_use()
  {
    var code = PromoCode.CreatePriceOverride("REL", 49m, maxTotalUses: 2);
    code.IncrementUses();
    code.IncrementUses();

    code.DecrementUses();

    Assert.Equal(1, code.CurrentUses);
  }

  [Fact]
  public void DecrementUses_never_goes_below_zero()
  {
    var code = PromoCode.CreatePriceOverride("ZERO", 49m, maxTotalUses: 1);

    code.DecrementUses();

    Assert.Equal(0, code.CurrentUses);
  }

  [Fact]
  public void DecrementUses_reopens_capacity_after_cap_reached()
  {
    // Mina #3: skasowanie porzuconego tenanta z redempcją musi ODDAĆ miejsce w MaxTotalUses.
    var code = PromoCode.CreatePriceOverride("REOPEN", 49m, maxTotalUses: 1);
    code.IncrementUses();
    Assert.False(code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, true).IsAllowed);

    code.DecrementUses();

    Assert.True(code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, true).IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_rejects_when_MaxUsesPerTenant_reached()
  {
    var code = PromoCode.CreatePriceOverride("ONCE", 49m, maxUsesPerTenant: 1);

    var v = code.CanBeRedeemed(DateTime.UtcNow, 1, SubscriptionStatus.Trial, true);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_rejects_NewTenantsOnly_for_existing()
  {
    var code = PromoCode.CreatePriceOverride("NEWONLY", 49m, appliesTo: PromoCodeAppliesTo.NewTenantsOnly);

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Active, isNewTenantRegistration: false);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_rejects_TrialExtension_for_non_trial_tenant()
  {
    var code = PromoCode.CreateTrialExtension("EXT", 30);

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Active, isNewTenantRegistration: true);

    Assert.False(v.IsAllowed);
  }

  [Fact]
  public void CanBeRedeemed_allows_valid_code_during_new_registration()
  {
    var code = PromoCode.CreatePriceOverride("OKAY", 49m);

    var v = code.CanBeRedeemed(DateTime.UtcNow, 0, SubscriptionStatus.Trial, isNewTenantRegistration: true);

    Assert.True(v.IsAllowed);
    Assert.Null(v.Reason);
  }

  [Fact]
  public void IncrementUses_throws_when_at_max()
  {
    var code = PromoCode.CreatePriceOverride("ONESHOT", 49m, maxTotalUses: 1);
    code.IncrementUses();

    Assert.Throws<InvalidOperationException>(() => code.IncrementUses());
  }
}
