using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Payments.Abstractions;
using App.Application.Tenants.Dtos;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Payments.Queries.GetMerchantAccountStatus;

/// <summary>
/// Odświeża stan konta płatności salonu u operatora (read-through: odpytuje operatora i persystuje
/// <c>charges_enabled</c> / status onboardingu) i zwraca DTO dla panelu „Zadatki".
/// </summary>
public record GetMerchantAccountStatusQuery : IRequest<MerchantAccountStatusDto>;

internal class GetMerchantAccountStatusQueryHandler
    : TenantHandler<GetMerchantAccountStatusQuery, MerchantAccountStatusDto>
{
  private readonly ITenantRepository _tenants;
  private readonly IUnitOfWork _uow;
  private readonly IPaymentProvider _payments;

  public GetMerchantAccountStatusQueryHandler(
    ICurrentTenantService currentTenantService,
    ITenantRepository tenants,
    IUnitOfWork uow,
    IPaymentProvider payments)
    : base(currentTenantService)
  {
    _tenants = tenants;
    _uow = uow;
    _payments = payments;
  }

  public override async Task<MerchantAccountStatusDto> Handle(GetMerchantAccountStatusQuery request, CancellationToken ct)
  {
    var tenant = await _tenants.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    if (tenant.MerchantAccount is null)
    {
      return MerchantAccountStatusDto.NotConnected();
    }

    // Odpytaj operatora i zsynchronizuj lokalny stan. Awaria operatora nie powinna wywracać panelu —
    // wtedy zwracamy ostatni znany stan.
    if (_payments.IsConfigured)
    {
      try
      {
        var status = await _payments.GetMerchantAccountStatusAsync(tenant.MerchantAccount.AccountId, ct);
        var onboarding = status is { ChargesEnabled: true, DetailsSubmitted: true }
          ? MerchantOnboardingStatus.Complete
          : MerchantOnboardingStatus.Pending;

        tenant.UpdateMerchantAccountStatus(onboarding, status.ChargesEnabled);
        _tenants.Update(tenant);
        await _uow.SaveChangesAsync(ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Błąd Stripe/sieci świadomie połykany — zwracamy ostatni znany stan (graceful degradation,
        // panel działa gdy Stripe pada). Anulowanie żądania (OperationCanceledException) propagujemy.
      }
    }

    return new MerchantAccountStatusDto(
      true,
      tenant.MerchantAccount.Provider,
      tenant.MerchantAccount.OnboardingStatus,
      tenant.MerchantAccount.CanAcceptPayments);
  }
}
