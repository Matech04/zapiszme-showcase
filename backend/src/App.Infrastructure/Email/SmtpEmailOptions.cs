namespace App.Infrastructure.Email;

/// <summary>
/// Opcje SMTP używane przez SmtpEmailTransport w trybie local-prod (Mailpit).
/// Wartości domyślne celują w typowy `mailpit` na docker-network'u (host=mailpit, port=1025).
/// </summary>
public sealed class SmtpEmailOptions
{
  public const string SectionName = "Smtp";

  public string Host { get; set; } = "mailpit";
  public int Port { get; set; } = 1025;
  public string SenderAddress { get; set; } = "noreply@booking.local";
  public string FeedbackRecipientEmail { get; set; } = "feedback@booking.local";
  public bool UseSsl { get; set; } = false;
}
