namespace App.Domain.Exceptions;

/// <summary>Za dużo nieudanych prób — kod zablokowany, user musi poprosić o resend.</summary>
public sealed class PhoneOtpLockedException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.PhoneOtpLocked;

  public PhoneOtpLockedException() : base("Zbyt wiele nieudanych prób. Poproś o nowy kod.") { }
}
