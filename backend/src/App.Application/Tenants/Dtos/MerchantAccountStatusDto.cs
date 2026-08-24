using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Tenants.Dtos;

/// <summary>
/// Stan konta płatności salonu dla panelu „Zadatki". <see cref="Connected"/>=false gdy salon
/// nie rozpoczął jeszcze onboardingu. <see cref="CanAcceptPayments"/> steruje dostępnością akcji
/// „Generuj zadatek".
/// </summary>
public record MerchantAccountStatusDto(
  bool Connected,
  string? Provider,
  MerchantOnboardingStatus OnboardingStatus,
  bool CanAcceptPayments)
{
  public static MerchantAccountStatusDto NotConnected() =>
    new(false, null, MerchantOnboardingStatus.NotStarted, false);
}
