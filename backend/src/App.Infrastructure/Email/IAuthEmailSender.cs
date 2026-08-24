namespace App.Infrastructure.Email;

public interface IAuthEmailSender
{
  Task SendConfirmEmailAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default);
  Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default);
  Task SendEmployeeInviteAsync(string toEmail, string inviteUrl, CancellationToken cancellationToken = default);

  /// <summary>Link potwierdzający zmianę adresu e-mail — wysyłany na NOWY adres.</summary>
  Task SendChangeEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default);
}
