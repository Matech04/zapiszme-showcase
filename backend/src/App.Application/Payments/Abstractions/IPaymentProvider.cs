namespace App.Application.Payments.Abstractions;

/// <summary>
/// Operator-neutralna abstrakcja płatności zadatku. Implementacja referencyjna: Stripe Connect
/// (direct charges — salon jest merchant of record, środki nigdy nie przechodzą przez platformę).
/// Per-tenant routing odbywa się przez <see cref="PaymentSessionRequest.MerchantAccountId"/>.
/// </summary>
public interface IPaymentProvider
{
  /// <summary>Identyfikator operatora zapisywany na koncie salonu, np. "stripe".</summary>
  string ProviderName { get; }

  /// <summary>Czy provider jest skonfigurowany (klucze obecne). Gdy false — funkcja zadatków nieaktywna globalnie.</summary>
  bool IsConfigured { get; }

  /// <summary>Tworzy sesję płatności na koncie salonu i zwraca link (hosted checkout) do wysłania klientce.</summary>
  Task<PaymentSessionResult> CreatePaymentSessionAsync(PaymentSessionRequest request, CancellationToken ct);

  /// <summary>Weryfikuje podpis i parsuje surowy payload webhooka do zdarzenia neutralnego.</summary>
  PaymentWebhookEvent ParseAndVerifyWebhook(string payload, string signatureHeader);

  /// <summary>
  /// Tworzy (jeśli trzeba) konto salonu u operatora i zwraca link onboardingu. Gdy
  /// <paramref name="existingAccountId"/> jest null — zakładane jest nowe konto.
  /// </summary>
  Task<MerchantOnboardingLink> CreateOnboardingLinkAsync(
    string? existingAccountId, Guid tenantId, string returnUrl, string refreshUrl, CancellationToken ct);

  /// <summary>Odpytuje operatora o aktualny stan konta salonu (czy może przyjmować płatności).</summary>
  Task<MerchantAccountStatus> GetMerchantAccountStatusAsync(string accountId, CancellationToken ct);

  /// <summary>Zwraca zadatek na koncie salonu (faza 2). Identyfikacja po sesji płatności.</summary>
  Task<RefundResult> RefundAsync(string paymentSessionId, string merchantAccountId, CancellationToken ct);
}

/// <summary>Żądanie utworzenia sesji płatności zadatku.</summary>
public record PaymentSessionRequest(
  Money Amount,
  Guid AppointmentId,
  Guid TenantId,
  string MerchantAccountId,
  string InstrumentLabel,
  string Description,
  string SuccessUrl,
  string CancelUrl,
  int ExpiresInHours = 24);

/// <summary>Wynik utworzenia sesji — link do wysłania klientce + termin ważności.</summary>
public record PaymentSessionResult(string SessionId, string Url, DateTime ExpiresAtUtc);

public enum PaymentWebhookEventType
{
  Ignored = 0,
  PaymentSucceeded = 1,
  PaymentFailed = 2,
  RefundCompleted = 3,
}

/// <summary>Neutralne zdarzenie webhooka — niezależne od operatora.</summary>
public record PaymentWebhookEvent(
  PaymentWebhookEventType Type,
  string? SessionId,
  Guid? AppointmentId,
  Guid? TenantId,
  string? ConnectedAccountId);

public record MerchantOnboardingLink(string AccountId, string Url);

public record MerchantAccountStatus(string AccountId, bool ChargesEnabled, bool DetailsSubmitted);

public record RefundResult(bool Success, string? RefundId);
