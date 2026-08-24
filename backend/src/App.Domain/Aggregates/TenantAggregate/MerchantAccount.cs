using App.Domain.Common;

namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Konto płatności salonu u operatora (np. Stripe Connect connected account). Owned type na
/// <see cref="Tenant"/>, nullable — null dopóki salon nie rozpocznie onboardingu. Przechowuje
/// wyłącznie identyfikator konta (NIE sekrety) — środki płyną bezpośrednio do salonu, platforma
/// nigdy ich nie trzyma (model off-platform → poza licencją KNF).
/// </summary>
public class MerchantAccount
{
  /// <summary>Identyfikator operatora, np. "stripe".</summary>
  public string Provider { get; private set; } = string.Empty;

  /// <summary>Identyfikator konta po stronie operatora (np. Stripe connected account id "acct_...").</summary>
  public string AccountId { get; private set; } = string.Empty;

  public MerchantOnboardingStatus OnboardingStatus { get; private set; } = MerchantOnboardingStatus.Pending;

  /// <summary>Czy operator potwierdził, że konto może przyjmować płatności (Stripe `charges_enabled`).</summary>
  public bool ChargesEnabled { get; private set; }

  private MerchantAccount() { }

  public MerchantAccount(string provider, string accountId)
  {
    Provider = Guard.NormalizeRequiredText(provider, nameof(provider)).ToLowerInvariant();
    AccountId = Guard.NormalizeRequiredText(accountId, nameof(accountId));
    OnboardingStatus = MerchantOnboardingStatus.Pending;
    ChargesEnabled = false;
  }

  public void UpdateStatus(MerchantOnboardingStatus onboardingStatus, bool chargesEnabled)
  {
    OnboardingStatus = onboardingStatus;
    ChargesEnabled = chargesEnabled;
  }

  /// <summary>Konto gotowe do przyjmowania zadatków (onboarding zakończony i płatności włączone).</summary>
  public bool CanAcceptPayments => ChargesEnabled;
}
