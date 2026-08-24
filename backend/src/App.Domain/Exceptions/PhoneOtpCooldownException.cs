namespace App.Domain.Exceptions;

/// <summary>Resend OTP zbyt szybko po poprzednim. Klient powinien poczekać <see cref="RetryAfterSeconds"/>.</summary>
public sealed class PhoneOtpCooldownException : Exception, IErrorCodeException
{
  public int RetryAfterSeconds { get; }
  public string ErrorCode => ErrorCodes.PhoneOtpCooldown;

  public PhoneOtpCooldownException(int retryAfterSeconds)
    : base($"Poczekaj {retryAfterSeconds}s przed ponownym wysłaniem kodu.")
  {
    RetryAfterSeconds = retryAfterSeconds;
  }
}
