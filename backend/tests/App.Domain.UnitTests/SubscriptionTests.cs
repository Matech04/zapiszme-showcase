using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

/// <summary>
/// TEN-009..TEN-018 — Subscription nowego modelu (per-seat, founding member, status lifecycle).
/// </summary>
public sealed class SubscriptionTests
{
  [Fact]
  public void StartTrial_returns_Trial_with_1_seat_and_30day_window()
  {
    var sub = Subscription.StartTrial();

    Assert.Equal(SubscriptionStatus.Trial, sub.Status);
    Assert.Equal(1, sub.Seats);
    Assert.False(sub.IsFoundingMember);
    Assert.NotNull(sub.TrialEndsAt);
    Assert.True(sub.TrialEndsAt > DateTimeOffset.UtcNow.AddDays(29));
    Assert.True(sub.TrialEndsAt <= DateTimeOffset.UtcNow.AddDays(30).AddSeconds(1));
    Assert.Null(sub.CurrentPeriodEndsAt);
    Assert.True(sub.IsTrialActive);
  }

  [Fact]
  public void EffectiveStatus_returns_PastDue_when_trial_expired()
  {
    var sub = Subscription.AdminReset(
      SubscriptionStatus.Trial,
      seats: 1,
      isFoundingMember: false,
      trialEndsAt: DateTimeOffset.UtcNow.AddDays(-1),
      currentPeriodEndsAt: null);

    Assert.Equal(SubscriptionStatus.Trial, sub.Status);
    Assert.Equal(SubscriptionStatus.PastDue, sub.EffectiveStatus);
    Assert.False(sub.IsTrialActive);
  }

  [Fact]
  public void EffectiveStatus_returns_Trial_when_TrialEndsAt_in_future()
  {
    var sub = Subscription.StartTrial(7);

    Assert.Equal(SubscriptionStatus.Trial, sub.EffectiveStatus);
    Assert.True(sub.IsTrialActive);
  }

  [Fact]
  public void Activate_sets_seats_and_currentPeriodEndsAt_and_clears_trial()
  {
    var sub = Subscription.StartTrial();

    sub.Activate(seats: 3, foundingMember: false);

    Assert.Equal(SubscriptionStatus.Active, sub.Status);
    Assert.Equal(3, sub.Seats);
    Assert.False(sub.IsFoundingMember);
    Assert.Null(sub.TrialEndsAt);
    Assert.NotNull(sub.CurrentPeriodEndsAt);
    Assert.True(sub.CurrentPeriodEndsAt > DateTimeOffset.UtcNow.AddDays(27));
  }

  [Fact]
  public void Activate_throws_for_seats_below_one()
  {
    var sub = Subscription.StartTrial();
    Assert.Throws<ArgumentOutOfRangeException>(() => sub.Activate(seats: 0, foundingMember: false));
  }

  [Fact]
  public void ChangeSeats_updates_value_and_price()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);

    sub.ChangeSeats(3);

    Assert.Equal(3, sub.Seats);
    // 79 + 35*2 = 149 zł = 14900 grosze
    Assert.Equal(14900, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void ChangeSeats_supports_downgrade()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 5, foundingMember: false);
    sub.ChangeSeats(2);

    Assert.Equal(2, sub.Seats);
    // 79 + 35 = 114 zł = 11400 grosze
    Assert.Equal(11400, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void ChangeSeats_throws_for_seats_below_one()
  {
    var sub = Subscription.StartTrial();
    Assert.Throws<ArgumentOutOfRangeException>(() => sub.ChangeSeats(0));
  }

  [Fact]
  public void MarkPastDue_changes_status_and_clears_period()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 2, foundingMember: false);

    sub.MarkPastDue();

    Assert.Equal(SubscriptionStatus.PastDue, sub.Status);
    Assert.Null(sub.CurrentPeriodEndsAt);
    Assert.Equal(2, sub.Seats);
  }

  [Fact]
  public void Cancel_changes_status_and_clears_period_but_keeps_seats_and_founding()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 4, foundingMember: true);

    sub.Cancel();

    Assert.Equal(SubscriptionStatus.Canceled, sub.Status);
    Assert.Equal(4, sub.Seats);
    Assert.True(sub.IsFoundingMember);
    Assert.Null(sub.CurrentPeriodEndsAt);
  }

  [Fact]
  public void MonthlyPriceInGrosze_standard_1seat_is_7900()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);
    Assert.Equal(7900, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void MonthlyPriceInGrosze_founding_1seat_is_4900()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: true);
    Assert.Equal(4900, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void MonthlyPriceInGrosze_founding_3seats_is_11900()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 3, foundingMember: true);
    // 49 + 35*2 = 119 zł = 11900 grosze
    Assert.Equal(11900, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void MonthlySmsAllowance_1seat_is_200()
  {
    var sub = Subscription.StartTrial();
    Assert.Equal(200, sub.MonthlySmsAllowance);
  }

  [Fact]
  public void MonthlySmsAllowance_scales_per_seat()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 3, foundingMember: false);
    // 200 + 150*2 = 500
    Assert.Equal(500, sub.MonthlySmsAllowance);
  }

  [Fact]
  public void MarkAsFoundingMember_flips_discount()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);
    Assert.Equal(7900, sub.MonthlyPriceInGrosze);

    sub.MarkAsFoundingMember();

    Assert.True(sub.IsFoundingMember);
    Assert.Equal(4900, sub.MonthlyPriceInGrosze);
  }

  [Fact]
  public void DaysRemainingInTrial_returns_zero_when_not_in_trial()
  {
    var sub = Subscription.StartTrial();
    sub.Activate(seats: 1, foundingMember: false);
    Assert.Equal(0, sub.DaysRemainingInTrial);
  }

  [Fact]
  public void DaysRemainingInTrial_returns_positive_during_trial()
  {
    var sub = Subscription.StartTrial(10);
    Assert.InRange(sub.DaysRemainingInTrial, 9, 10);
  }
}
