namespace App.Domain.Exceptions;

/// <summary>Próba resendu kodu dla już potwierdzonego telefonu.</summary>
public sealed class PhoneAlreadyConfirmedException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.PhoneOtpAlreadyConfirmed;

  public PhoneAlreadyConfirmedException() : base("Numer telefonu jest już potwierdzony.") { }
}
