namespace App.Domain.Exceptions;

/// <summary>
/// Wysyłka SMS przez smsapi.pl nie udała się (brak konfiguracji tokenu, błąd API lub sieci).
/// Mapowany na 503. Konto użytkownika jest dalej w stanie spójnym — można retry przez
/// <c>resend-phone-otp</c>.
/// </summary>
public sealed class SmsServiceUnavailableException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.SmsServiceUnavailable;

  public SmsServiceUnavailableException(string detail, Exception? inner = null)
    : base($"Nie udało się wysłać SMS-a: {detail}", inner)
  {
  }
}
