namespace App.Infrastructure.Payments.Stripe;

/// <summary>
/// Konfiguracja Stripe Connect. Sekcja <c>Payments:Stripe</c> w <c>appsettings.json</c>; sekrety
/// ładowane ze zmiennych środowiskowych (<c>PAYMENTS__STRIPE__SECRETKEY</c>,
/// <c>PAYMENTS__STRIPE__WEBHOOKSECRET</c>). Pusty <see cref="SecretKey"/> = funkcja zadatków wyłączona.
/// </summary>
public sealed class StripePaymentOptions
{
  /// <summary>Sekretny klucz platformy (sk_live_… / sk_test_…). Pusty = provider nieskonfigurowany.</summary>
  public string SecretKey { get; set; } = string.Empty;

  /// <summary>Sekret podpisu Connect webhooka (whsec_…) do weryfikacji <c>Stripe-Signature</c>.</summary>
  public string WebhookSecret { get; set; } = string.Empty;

  /// <summary>Kod kraju zakładanych kont Express. Default <c>PL</c>.</summary>
  public string AccountCountry { get; set; } = "PL";

  /// <summary>
  /// Metody płatności udostępniane w Checkout. Pusty default — wartości pochodzą z configu.
  /// (Nie inicjujemy listy, bo binder konfiguracji DOKLEJA do niepustej kolekcji zamiast zastąpić,
  /// co dawało duplikaty „card"). Provider stosuje fallback ["card","blik"] gdy pusto i dedupe.
  /// </summary>
  public List<string> PaymentMethodTypes { get; set; } = new();
}
