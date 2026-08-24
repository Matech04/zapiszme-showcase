namespace App.Application.Notifications;

/// <summary>
/// Adresat powiadomienia. Każdy kanał używa pola, którego potrzebuje (e-mail → <see cref="Email"/>,
/// SMS → <see cref="Phone"/>, in-app → <see cref="UserId"/>). Pola nieużywane przez dany kanał
/// mogą być null — kanał wtedy po prostu pomija wysyłkę.
/// </summary>
public record NotificationRecipient(
  string? Email,
  string? Phone,
  Guid? UserId,
  string DisplayName);
