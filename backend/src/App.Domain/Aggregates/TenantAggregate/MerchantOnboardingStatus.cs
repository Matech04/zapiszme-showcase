namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>Stan onboardingu konta płatności salonu u operatora (np. Stripe Connect Express).</summary>
public enum MerchantOnboardingStatus
{
  /// <summary>Konto jeszcze nie utworzone / onboarding nierozpoczęty.</summary>
  NotStarted = 0,

  /// <summary>Konto utworzone, ale onboarding niedokończony — płatności jeszcze niemożliwe.</summary>
  Pending = 1,

  /// <summary>Onboarding zakończony — operator potwierdził możliwość przyjmowania płatności.</summary>
  Complete = 2,
}
