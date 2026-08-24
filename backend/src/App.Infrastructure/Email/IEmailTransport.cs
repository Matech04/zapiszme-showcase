namespace App.Infrastructure.Email;

/// <summary>
/// Pojedyncza warstwa transportu e-mail — jedyne miejsce, które wie *jak* fizycznie
/// wysłać wiadomość (Azure Communication Services w prod, SMTP/Mailpit w local-prod).
/// Sendery (auth / OTP / powiadomienia / feedback) budują tylko treść i wołają ten interfejs.
/// </summary>
public interface IEmailTransport
{
  /// <summary>
  /// Wysyła wiadomość jako multipart (HTML + tekst). Rzuca wyjątek przy błędzie transportu.
  /// Jeśli transport nie jest skonfigurowany — loguje ostrzeżenie i cicho wraca (ścieżka dev/test;
  /// produkcja jest chroniona walidacją startową w Program.cs).
  /// </summary>
  Task SendAsync(string toEmail, string subject, EmailBody body, CancellationToken ct = default);

  /// <summary>Adres odbiorcy zgłoszeń feedback (błędy / propozycje). Null/puste gdy nieskonfigurowany.</summary>
  string? FeedbackRecipientEmail { get; }
}
