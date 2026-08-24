using App.Infrastructure.Email;

namespace App.Api.E2eSupport;

/// <summary>
/// Testowy <see cref="IEmailTransport"/> — przechwytuje wysyłki zamiast realnego transportu.
/// Przez ten transport idzie kanał e-mail systemu powiadomień (<c>EmailNotificationChannel</c>).
/// </summary>
public sealed class TestEmailTransport : IEmailTransport
{
  private readonly List<(string To, string Subject, string Html, string Text)> _sent = new();
  private readonly object _gate = new();

  public string? FeedbackRecipientEmail => "feedback@test.local";

  public IReadOnlyList<(string To, string Subject, string Html, string Text)> Sent
  {
    get
    {
      lock (_gate) { return _sent.ToArray(); }
    }
  }

  public Task SendAsync(string toEmail, string subject, EmailBody body, CancellationToken ct = default)
  {
    lock (_gate) { _sent.Add((toEmail, subject, body.Html, body.Text)); }
    return Task.CompletedTask;
  }
}
