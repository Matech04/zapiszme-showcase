using System.Text.Json;
using App.Application.Payments.Abstractions;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Payments.Dev;

/// <summary>
/// Dev-only <see cref="IPaymentProvider"/> — pozwala klikać zadatki lokalnie bez konta Stripe.
/// Włączany flagą <c>Payments:DevFakeProvider=true</c>, NIGDY w Production (guard w Program.cs).
///
/// Zachowanie:
///  - onboarding kończy się natychmiast (link = returnUrl, powrót prosto do panelu),
///  - konto zawsze raportuje <c>charges_enabled</c>, więc zadatki są dostępne na wizytach,
///  - „checkout" prowadzi na naszą stronę sukcesu (bez realnej płatności).
///
/// Wizyta NIE zmienia się w „opłacona" samoczynnie — to robi webhook. W dev można go wywołać
/// ręcznie prostym JSON-em (podpis nie jest weryfikowany):
/// <code>
/// curl -X POST http://localhost:5178/api/payments/webhook -H 'Content-Type: application/json' \
///   -d '{"type":"PaymentSucceeded","sessionId":"cs_dev_&lt;appointmentId:N&gt;","appointmentId":"...","tenantId":"..."}'
/// </code>
/// </summary>
public sealed class DevFakePaymentProvider : IPaymentProvider
{
  private readonly ILogger<DevFakePaymentProvider> _logger;

  public DevFakePaymentProvider(ILogger<DevFakePaymentProvider> logger)
  {
    _logger = logger;
    _logger.LogWarning(
      "Płatności: aktywny DevFakePaymentProvider (Payments:DevFakeProvider=true). "
      + "Żadna płatność nie jest realna. Nie używać poza dev.");
  }

  /// <summary>Ta sama nazwa co realny provider — konto salonu wygląda identycznie w bazie.</summary>
  public string ProviderName => "stripe";

  public bool IsConfigured => true;

  public Task<MerchantOnboardingLink> CreateOnboardingLinkAsync(
    string? existingAccountId, Guid tenantId, string returnUrl, string refreshUrl, CancellationToken ct)
  {
    var accountId = existingAccountId ?? $"acct_dev_{tenantId:N}";
    // Link onboardingu prowadzi od razu z powrotem do panelu — jedno kliknięcie i konto jest „gotowe".
    return Task.FromResult(new MerchantOnboardingLink(accountId, returnUrl));
  }

  public Task<MerchantAccountStatus> GetMerchantAccountStatusAsync(string accountId, CancellationToken ct)
    => Task.FromResult(new MerchantAccountStatus(accountId, ChargesEnabled: true, DetailsSubmitted: true));

  public Task<PaymentSessionResult> CreatePaymentSessionAsync(PaymentSessionRequest request, CancellationToken ct)
  {
    var sessionId = $"cs_dev_{request.AppointmentId:N}";
    _logger.LogWarning(
      "Płatności (dev): pozorna sesja {SessionId} na {Amount} {Currency} — bez realnego obciążenia.",
      sessionId, request.Amount.Amount, request.Amount.Currency);

    return Task.FromResult(new PaymentSessionResult(sessionId, request.SuccessUrl, DateTime.UtcNow.AddHours(24)));
  }

  /// <summary>
  /// Bez weryfikacji podpisu — payload to nasz prosty JSON:
  /// <c>{"type":"PaymentSucceeded","sessionId":"...","appointmentId":"...","tenantId":"..."}</c>.
  /// </summary>
  public PaymentWebhookEvent ParseAndVerifyWebhook(string payload, string signatureHeader)
  {
    using var doc = JsonDocument.Parse(payload);
    var root = doc.RootElement;

    var type = root.TryGetProperty("type", out var t) && Enum.TryParse<PaymentWebhookEventType>(t.GetString(), out var parsed)
      ? parsed
      : PaymentWebhookEventType.Ignored;
    string? sessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
    Guid? appointmentId = root.TryGetProperty("appointmentId", out var a) && Guid.TryParse(a.GetString(), out var ag)
      ? ag
      : null;
    Guid? tenantId = root.TryGetProperty("tenantId", out var tn) && Guid.TryParse(tn.GetString(), out var tg)
      ? tg
      : null;
    string? connectedAccountId = root.TryGetProperty("connectedAccountId", out var ca) ? ca.GetString() : null;

    return new PaymentWebhookEvent(type, sessionId, appointmentId, tenantId, connectedAccountId);
  }

  public Task<RefundResult> RefundAsync(string paymentSessionId, string merchantAccountId, CancellationToken ct)
    => Task.FromResult(new RefundResult(true, "re_dev"));
}
