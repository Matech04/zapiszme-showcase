namespace App.Domain.Exceptions;

/// <summary>Kod SMS wygasł (poza TTL). User musi poprosić o resend.</summary>
public sealed class PhoneOtpExpiredException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.PhoneOtpExpired;

  public PhoneOtpExpiredException() : base("Kod SMS wygasł. Poproś o nowy.") { }
}
