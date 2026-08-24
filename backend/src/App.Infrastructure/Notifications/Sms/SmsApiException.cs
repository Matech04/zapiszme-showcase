namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Błąd zwrócony przez smsapi.pl. <see cref="ErrorCode"/> = pole <c>error</c> z JSON-a;
/// pełna lista kodów: https://www.smsapi.pl/docs/#error-codes.
/// </summary>
public sealed class SmsApiException : Exception
{
  public int ErrorCode { get; }

  public SmsApiException(int errorCode, string message) : base(message)
  {
    ErrorCode = errorCode;
  }
}
