using App.Infrastructure.Email;

namespace App.Api.E2eSupport;

public sealed class TestAuthEmailMailbox : IAuthEmailSender
{
  private readonly List<(string ToEmail, string Url)> _passwordResetsSent = new();
  private readonly List<(string ToEmail, string Url)> _confirmEmailsSent = new();
  private readonly object _gate = new();

  public string? LastConfirmEmailUrl { get; private set; }
  public string? LastPasswordResetUrl { get; private set; }
  public string? LastEmployeeInviteUrl { get; private set; }
  public string? LastChangeEmailConfirmationUrl { get; private set; }

  /// <summary>Wszystkie wysłane password-reset (ToEmail, Url) — w kolejności wysyłki.</summary>
  public IReadOnlyList<(string ToEmail, string Url)> PasswordResetsSent
  {
    get
    {
      lock (_gate) { return _passwordResetsSent.ToArray(); }
    }
  }

  /// <summary>Wszystkie wysłane confirm-email (ToEmail, Url) — w kolejności wysyłki.</summary>
  public IReadOnlyList<(string ToEmail, string Url)> ConfirmEmailsSent
  {
    get
    {
      lock (_gate) { return _confirmEmailsSent.ToArray(); }
    }
  }

  public Task SendConfirmEmailAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
  {
    lock (_gate) { _confirmEmailsSent.Add((toEmail, confirmUrl)); }
    LastConfirmEmailUrl = confirmUrl;
    return Task.CompletedTask;
  }

  public Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
  {
    lock (_gate) { _passwordResetsSent.Add((toEmail, resetUrl)); }
    LastPasswordResetUrl = resetUrl;
    return Task.CompletedTask;
  }

  public Task SendEmployeeInviteAsync(string toEmail, string inviteUrl, CancellationToken cancellationToken = default)
  {
    LastEmployeeInviteUrl = inviteUrl;
    return Task.CompletedTask;
  }

  public Task SendChangeEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
  {
    LastChangeEmailConfirmationUrl = confirmUrl;
    return Task.CompletedTask;
  }
}
