using System.Text.Json;
using App.Application.Payments.Abstractions;

namespace App.Api.E2eSupport;

/// <summary>
/// Testowy <see cref="IPaymentProvider"/> — bez realnego Stripe. Zwraca deterministyczną sesję,
/// a webhook parsuje z PROSTEGO JSON-a kontrolowanego przez test (pomija weryfikację podpisu).
/// <see cref="ChargesEnabled"/> steruje gotowością konta (fallback przy generowaniu zadatku).
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
  public bool ChargesEnabled { get; set; } = true;
  public PaymentSessionRequest? LastRequest { get; private set; }

  public string ProviderName => "stripe";
  public bool IsConfigured => true;

  public Task<PaymentSessionResult> CreatePaymentSessionAsync(PaymentSessionRequest request, CancellationToken ct)
  {
    LastRequest = request;
    var sessionId = "cs_fake_" + request.AppointmentId.ToString("N");
    var result = new PaymentSessionResult(
      sessionId,
      "https://stripe.test/checkout/" + sessionId,
      DateTime.UtcNow.AddHours(24));
    return Task.FromResult(result);
  }

  /// <summary>
  /// Payload w testach to nasz JSON:
  /// <c>{"type":"PaymentSucceeded","sessionId":"...","appointmentId":"...","tenantId":"..."}</c>.
  /// </summary>
  public PaymentWebhookEvent ParseAndVerifyWebhook(string payload, string signatureHeader)
  {
    using var doc = JsonDocument.Parse(payload);
    var root = doc.RootElement;

    var type = Enum.Parse<PaymentWebhookEventType>(root.GetProperty("type").GetString()!);
    string? sessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
    Guid? appointmentId = root.TryGetProperty("appointmentId", out var a)
      && Guid.TryParse(a.GetString(), out var g) ? g : null;
    Guid? tenantId = root.TryGetProperty("tenantId", out var t)
      && Guid.TryParse(t.GetString(), out var tg) ? tg : null;
    string? connectedAccountId = root.TryGetProperty("connectedAccountId", out var ca) ? ca.GetString() : null;

    return new PaymentWebhookEvent(type, sessionId, appointmentId, tenantId, connectedAccountId);
  }

  public Task<MerchantOnboardingLink> CreateOnboardingLinkAsync(
    string? existingAccountId, Guid tenantId, string returnUrl, string refreshUrl, CancellationToken ct)
    => Task.FromResult(new MerchantOnboardingLink(existingAccountId ?? "acct_fake", "https://stripe.test/onboarding"));

  public Task<MerchantAccountStatus> GetMerchantAccountStatusAsync(string accountId, CancellationToken ct)
    => Task.FromResult(new MerchantAccountStatus(accountId, ChargesEnabled, true));

  public Task<RefundResult> RefundAsync(string paymentSessionId, string merchantAccountId, CancellationToken ct)
    => Task.FromResult(new RefundResult(true, "re_fake"));
}
