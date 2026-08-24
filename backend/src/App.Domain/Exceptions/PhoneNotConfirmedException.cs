namespace App.Domain.Exceptions;

/// <summary>
/// Login odrzucony — użytkownik zalogował się hasłem i ma potwierdzony email, ale numer
/// telefonu jeszcze nie jest potwierdzony kodem SMS. UI przekierowuje na <c>/confirm-phone</c>.
/// </summary>
public sealed class PhoneNotConfirmedException : Exception, IErrorCodeException
{
  public Guid UserId { get; }
  public string ErrorCode => ErrorCodes.PhoneNotConfirmed;

  public PhoneNotConfirmedException(Guid userId)
    : base("Numer telefonu nie został jeszcze potwierdzony. Wpisz kod, który wysłaliśmy SMS-em.")
  {
    UserId = userId;
  }
}
