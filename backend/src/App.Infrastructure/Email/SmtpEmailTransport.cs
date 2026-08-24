using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace App.Infrastructure.Email;

/// <summary>
/// Transport e-mail przez SMTP (MailKit) — używany w trybie local-prod (EMAIL_SENDER_MODE=Smtp).
/// Domyślny cel to Mailpit na docker-network'u, ale dowolny serwer SMTP (np. Postfix relay)
/// zadziała tak samo.
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
  private readonly IOptions<SmtpEmailOptions> _options;
  private readonly ILogger<SmtpEmailTransport> _logger;

  public SmtpEmailTransport(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailTransport> logger)
  {
    _options = options;
    _logger = logger;
  }

  public string? FeedbackRecipientEmail => _options.Value.FeedbackRecipientEmail;

  public async Task SendAsync(string toEmail, string subject, EmailBody body, CancellationToken ct = default)
  {
    var opts = _options.Value;
    var msg = new MimeMessage();
    msg.From.Add(MailboxAddress.Parse(opts.SenderAddress));
    msg.To.Add(MailboxAddress.Parse(toEmail));
    msg.Subject = subject;
    msg.Body = new BodyBuilder { HtmlBody = body.Html, TextBody = body.Text }.ToMessageBody();

    try
    {
      using var smtp = new SmtpClient();
      // Twardy deadline (ms). Bez tego MailKit czeka domyślnie 120s na zawieszonym serwerze SMTP,
      // trzymając wątek — token `ct` żądania zwykle nie ma własnego terminu. Spójne z ACS (10s).
      smtp.Timeout = 10_000;
      var socketOptions = opts.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
      await smtp.ConnectAsync(opts.Host, opts.Port, socketOptions, ct);
      await smtp.SendAsync(msg, ct);
      await smtp.DisconnectAsync(quit: true, ct);
    }
    catch (Exception ex)
    {
      // Bez obiektu wyjątku: enricher maskuje właściwości zdarzenia, ale nie LogEvent.Exception,
      // a komunikat serwera SMTP potrafi zawierać adres odbiorcy (PII).
      _logger.LogError(
        "Błąd wysyłki e-mail (SMTP): subject={Subject} to={To} host={Host}:{Port} typ={ExceptionType}",
        subject,
        toEmail,
        opts.Host,
        opts.Port,
        ex.GetType().Name);
      throw;
    }
  }
}
