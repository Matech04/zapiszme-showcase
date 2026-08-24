using App.Application.Payments.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace App.Infrastructure.Payments.Stripe;

/// <summary>
/// Implementacja <see cref="IPaymentProvider"/> oparta o Stripe Connect (direct charges). Używa
/// jednego klucza platformy + nagłówka <c>Stripe-Account</c> per żądanie — środki trafiają wprost
/// na konto salonu (salon = merchant of record). BLIK + karta, hosted Checkout, link ważny do 24h.
/// </summary>
public sealed class StripeConnectPaymentProvider : IPaymentProvider
{
  // Stripe wymaga expires_at w przedziale 30 min – 24h. Bufor poniżej górnego limitu chroni przed
  // odrzuceniem przy opóźnieniu/skew zegara.
  private static readonly TimeSpan MaxLinkTtl = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5);

  private readonly StripePaymentOptions _options;
  private readonly ILogger<StripeConnectPaymentProvider> _logger;
  private readonly StripeClient? _client;

  public StripeConnectPaymentProvider(
    HttpClient httpClient,
    IOptions<StripePaymentOptions> options,
    ILogger<StripeConnectPaymentProvider> logger)
  {
    _options = options.Value;
    _logger = logger;
    // Timeout konfigurowany na HttpClient (AddHttpClient, 10s) zamiast domyślnych 80s Stripe.net.
    // maxNetworkRetries: 1 (nie 2) — `GetMerchantAccountStatusAsync` jest na gorącej ścieżce panelu
    // (każde otwarcie zakładki Zadatki); ogranicza worst-case przy awarii Stripe do ~20s zamiast ~30s.
    _client = string.IsNullOrWhiteSpace(_options.SecretKey)
      ? null
      : new StripeClient(_options.SecretKey, httpClient: new SystemNetHttpClient(httpClient, maxNetworkRetries: 1));
  }

  public string ProviderName => "stripe";

  public bool IsConfigured => _client is not null;

  public async Task<PaymentSessionResult> CreatePaymentSessionAsync(PaymentSessionRequest request, CancellationToken ct)
  {
    var client = RequireClient();

    int hours = Math.Clamp(request.ExpiresInHours, 1, 24);
    DateTime expiresAt = DateTime.UtcNow.AddHours(hours);
    if (expiresAt - DateTime.UtcNow > MaxLinkTtl)
    {
      expiresAt = DateTime.UtcNow.Add(MaxLinkTtl);
    }

    long unitAmountMinor = (long)Math.Round(request.Amount.Amount * 100m, MidpointRounding.AwayFromZero);

    // Fallback + dedupe: pusty config → karta+BLIK; usuń ewentualne duplikaty (Stripe odrzuca powtórki).
    var methodTypes = (_options.PaymentMethodTypes is { Count: > 0 }
        ? _options.PaymentMethodTypes
        : new List<string> { "card", "blik" })
      .Where(m => !string.IsNullOrWhiteSpace(m))
      .Select(m => m.Trim().ToLowerInvariant())
      .Distinct()
      .ToList();

    var metadata = new Dictionary<string, string>
    {
      ["appointmentId"] = request.AppointmentId.ToString(),
      ["tenantId"] = request.TenantId.ToString(),
      ["instrument"] = request.InstrumentLabel,
    };

    var options = new SessionCreateOptions
    {
      Mode = "payment",
      PaymentMethodTypes = methodTypes,
      ExpiresAt = expiresAt,
      SuccessUrl = request.SuccessUrl,
      CancelUrl = request.CancelUrl,
      Metadata = metadata,
      LineItems = new List<SessionLineItemOptions>
      {
        new()
        {
          Quantity = 1,
          PriceData = new SessionLineItemPriceDataOptions
          {
            Currency = request.Amount.Currency.ToLowerInvariant(),
            UnitAmount = unitAmountMinor,
            ProductData = new SessionLineItemPriceDataProductDataOptions
            {
              Name = request.Description,
            },
          },
        },
      },
      PaymentIntentData = new SessionPaymentIntentDataOptions
      {
        Metadata = metadata,
      },
    };

    var requestOptions = new RequestOptions { StripeAccount = request.MerchantAccountId };

    var sessionService = new SessionService(client);
    Session session = await sessionService.CreateAsync(options, requestOptions, ct);

    _logger.LogInformation(
      "Utworzono sesję płatności zadatku {SessionId} dla wizyty {AppointmentId} na koncie {Account}.",
      session.Id, request.AppointmentId, request.MerchantAccountId);

    return new PaymentSessionResult(session.Id, session.Url, expiresAt);
  }

  public PaymentWebhookEvent ParseAndVerifyWebhook(string payload, string signatureHeader)
  {
    if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
    {
      throw new InvalidOperationException("Brak skonfigurowanego Payments:Stripe:WebhookSecret.");
    }

    Event stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);

    switch (stripeEvent.Type)
    {
      case "checkout.session.completed":
      case "checkout.session.async_payment_succeeded":
        {
          var session = stripeEvent.Data.Object as Session;
          if (session is null || !string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
          {
            return Ignored(stripeEvent);
          }

          return new PaymentWebhookEvent(
            PaymentWebhookEventType.PaymentSucceeded,
            session.Id,
            ParseGuid(session.Metadata, "appointmentId"),
            ParseGuid(session.Metadata, "tenantId"),
            stripeEvent.Account);
        }

      case "checkout.session.expired":
      case "checkout.session.async_payment_failed":
        {
          var session = stripeEvent.Data.Object as Session;
          return new PaymentWebhookEvent(
            PaymentWebhookEventType.PaymentFailed,
            session?.Id,
            ParseGuid(session?.Metadata, "appointmentId"),
            ParseGuid(session?.Metadata, "tenantId"),
            stripeEvent.Account);
        }

      case "charge.refunded":
        return new PaymentWebhookEvent(
          PaymentWebhookEventType.RefundCompleted, null, null, null, stripeEvent.Account);

      default:
        return Ignored(stripeEvent);
    }
  }

  public async Task<MerchantOnboardingLink> CreateOnboardingLinkAsync(
    string? existingAccountId, Guid tenantId, string returnUrl, string refreshUrl, CancellationToken ct)
  {
    var client = RequireClient();

    string accountId = existingAccountId ?? string.Empty;
    if (string.IsNullOrWhiteSpace(accountId))
    {
      var accountService = new AccountService(client);
      var account = await accountService.CreateAsync(new AccountCreateOptions
      {
        Type = "express",
        Country = _options.AccountCountry,
        Capabilities = new AccountCapabilitiesOptions
        {
          CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
          Transfers = new AccountCapabilitiesTransfersOptions { Requested = true },
          BlikPayments = new AccountCapabilitiesBlikPaymentsOptions { Requested = true },
        },
        Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
      }, cancellationToken: ct);
      accountId = account.Id;
    }

    var linkService = new AccountLinkService(client);
    var link = await linkService.CreateAsync(new AccountLinkCreateOptions
    {
      Account = accountId,
      RefreshUrl = refreshUrl,
      ReturnUrl = returnUrl,
      Type = "account_onboarding",
    }, cancellationToken: ct);

    return new MerchantOnboardingLink(accountId, link.Url);
  }

  public async Task<MerchantAccountStatus> GetMerchantAccountStatusAsync(string accountId, CancellationToken ct)
  {
    var client = RequireClient();
    var accountService = new AccountService(client);
    var account = await accountService.GetAsync(accountId, cancellationToken: ct);
    return new MerchantAccountStatus(account.Id, account.ChargesEnabled, account.DetailsSubmitted);
  }

  public async Task<RefundResult> RefundAsync(string paymentSessionId, string merchantAccountId, CancellationToken ct)
  {
    var client = RequireClient();
    var requestOptions = new RequestOptions { StripeAccount = merchantAccountId };

    var sessionService = new SessionService(client);
    var session = await sessionService.GetAsync(paymentSessionId, requestOptions: requestOptions, cancellationToken: ct);
    if (string.IsNullOrWhiteSpace(session.PaymentIntentId))
    {
      return new RefundResult(false, null);
    }

    var refundService = new RefundService(client);
    var refund = await refundService.CreateAsync(
      new RefundCreateOptions { PaymentIntent = session.PaymentIntentId },
      requestOptions,
      ct);

    return new RefundResult(true, refund.Id);
  }

  private StripeClient RequireClient() =>
    _client ?? throw new InvalidOperationException(
      "Stripe nie jest skonfigurowany (brak Payments:Stripe:SecretKey).");

  private static PaymentWebhookEvent Ignored(Event stripeEvent) =>
    new(PaymentWebhookEventType.Ignored, null, null, null, stripeEvent.Account);

  private static Guid? ParseGuid(IDictionary<string, string>? metadata, string key)
  {
    if (metadata is not null && metadata.TryGetValue(key, out var value) && Guid.TryParse(value, out var guid))
    {
      return guid;
    }

    return null;
  }
}
