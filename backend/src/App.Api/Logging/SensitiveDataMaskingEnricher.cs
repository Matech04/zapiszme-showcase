using Serilog.Core;
using Serilog.Events;

namespace App.Api.Logging;

/// <summary>
/// SEC-047 — maskuje właściwości log-eventu, których nazwa wskazuje na dane wrażliwe
/// (hasła, tokeny, OTP, klucze API, connection-stringi). Działa jako safety-net dla
/// sytuacji, gdy ktoś przez przypadek przekaże takie pole do <c>_logger.LogXxx(...)</c>.
///
/// Maskuje WARTOŚĆ; klucz pozostaje (pozwala wykryć regresję poprzez ścieżkę logu).
/// </summary>
public sealed class SensitiveDataMaskingEnricher : ILogEventEnricher
{
  // Klucze case-insensitive — każdy fragment "zawiera się w nazwie property" jest wystarczający.
  // Lista bazuje na realnych nazwach pól w DTO/handlerach tej aplikacji (Password, Token, OTP itp.).
  private static readonly string[] SensitiveNameFragments =
  [
    "password",
    "passwordhash",
    "secret",
    "apikey",
    "token",         // Token, AccessToken, RefreshToken, TurnstileToken, ResetToken
    "otp",           // Otp, OtpCode, OtpHash, sixDigitCode
    "authorization", // header
    "cookie",        // header
    "connectionstring",
    "securitystamp",
    "concurrencystamp",
    "phone",         // PhoneNumber, PhoneE164 (PII — RODO). UWAGA: maskuje też PhoneNumberConfirmed.
    "email",         // Email, ToEmail, EmailConfirmed (PII — RODO). Adres e-mail nie może trafiać do Seq cleartext.
    "mail",          // To/Recipient mail aliasy; łapie też pola z "mail" w nazwie.
    // PII imion/nazwisk/notatek klienta — fragmenty specyficzne (nie bare "name", by nie maskować
    // ServiceName/SalonName/TenantName, które nie są danymi osobowymi i bywają potrzebne w logach).
    "firstname",
    "lastname",
    "fullname",
    "displayname",
    "customername",
    "notes",         // AppointmentNotes, GeneralNotes — mogą zawierać PII.
    "sessionid",     // Stripe Checkout session id (cs_…) — identyfikator płatności; nie do logów
                     // zewnętrznych (Loki/Grafana Cloud) cleartext. NIE maskujemy "account" (acct_…
                     // to operacyjny id konta salonu) ani AppointmentId (nasz GUID, do korelacji).
    "actionurl",     // Link do zapłaty zadatku. Skracacz smsapi (idz.do) generuje odnośnik UNIKALNY
    "paymentlink",   // per odbiorca, więc w logu jest on de facto identyfikatorem konkretnej klientki.
  ];

  private const string MaskedValue = "***";

  public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
  {
    // LogEvent.Properties to ReadOnlyDictionary — używamy AddOrUpdateProperty z scalar.
    foreach (var property in logEvent.Properties.ToArray())
    {
      if (IsSensitiveKey(property.Key))
      {
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(property.Key, MaskedValue));
      }
    }
  }

  private static bool IsSensitiveKey(string propertyName)
  {
    foreach (var fragment in SensitiveNameFragments)
    {
      if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }
}
