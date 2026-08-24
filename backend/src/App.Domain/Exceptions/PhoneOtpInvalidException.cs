namespace App.Domain.Exceptions;

/// <summary>Wpisany kod jest niepoprawny. <see cref="RemainingAttempts"/> wskazuje ile prób zostało.</summary>
public sealed class PhoneOtpInvalidException : Exception, IErrorCodeException
{
  public int RemainingAttempts { get; }
  public string ErrorCode => ErrorCodes.PhoneOtpInvalid;

  public PhoneOtpInvalidException(int remainingAttempts)
    : base("Nieprawidłowy kod SMS.")
  {
    RemainingAttempts = remainingAttempts;
  }
}
