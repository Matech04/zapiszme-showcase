using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Payments.Abstractions;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace App.Application.Payments.Commands.ConnectMerchantAccount;

/// <summary>
/// Rozpoczyna/ponawia onboarding konta płatności salonu u operatora (Stripe Connect Express)
/// i zwraca link, na który należy przekierować właściciela. Po powrocie status konta odświeża
/// <c>GetMerchantAccountStatusQuery</c>.
/// </summary>
public record ConnectMerchantAccountCommand : IRequest<ConnectMerchantAccountResult>;

public record ConnectMerchantAccountResult(string OnboardingUrl);

internal class ConnectMerchantAccountCommandHandler
    : TenantHandler<ConnectMerchantAccountCommand, ConnectMerchantAccountResult>
{
  private readonly ITenantRepository _tenants;
  private readonly IUnitOfWork _uow;
  private readonly IPaymentProvider _payments;
  private readonly IConfiguration _config;

  public ConnectMerchantAccountCommandHandler(
    ICurrentTenantService currentTenantService,
    ITenantRepository tenants,
    IUnitOfWork uow,
    IPaymentProvider payments,
    IConfiguration config)
    : base(currentTenantService)
  {
    _tenants = tenants;
    _uow = uow;
    _payments = payments;
    _config = config;
  }

  public override async Task<ConnectMerchantAccountResult> Handle(ConnectMerchantAccountCommand request, CancellationToken ct)
  {
    if (!_payments.IsConfigured)
    {
      throw new AppointmentBookingRuleException(
        "Płatności nie są skonfigurowane na serwerze.", ErrorCodes.MerchantAccountNotReady);
    }

    var tenant = await _tenants.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var dashboardBaseUrl = (_config["Dashboard:BaseUrl"] ?? string.Empty).TrimEnd('/');
    var returnUrl = $"{dashboardBaseUrl}/admin/settings?tab=deposits&stripe=return";
    var refreshUrl = $"{dashboardBaseUrl}/admin/settings?tab=deposits&stripe=refresh";

    var link = await _payments.CreateOnboardingLinkAsync(
      tenant.MerchantAccount?.AccountId, TenantId, returnUrl, refreshUrl, ct);

    if (tenant.MerchantAccount is null || tenant.MerchantAccount.AccountId != link.AccountId)
    {
      tenant.ConnectMerchantAccount(_payments.ProviderName, link.AccountId);
      _tenants.Update(tenant);
      await _uow.SaveChangesAsync(ct);
    }

    return new ConnectMerchantAccountResult(link.Url);
  }
}
