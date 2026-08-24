namespace App.Domain.Exceptions;

/// <summary>
/// Próba potwierdzenia telefonu zanim email zostanie potwierdzony. Gate przeciwspamowy —
/// SMS OTP jest dostępny dopiero po kliknięciu linku z maila.
/// </summary>
public sealed class PhoneOtpEmailNotConfirmedException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.PhoneOtpEmailNotConfirmed;

  public PhoneOtpEmailNotConfirmedException()
    : base("Najpierw potwierdź adres email — kod SMS będzie dostępny po potwierdzeniu maila.")
  {
  }
}
