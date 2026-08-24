namespace App.Domain.Exceptions;

/// <summary>Weryfikacja OTP przez numer telefonu jest tymczasowo wyłączona w konfiguracji produktu.</summary>
public sealed class CustomerVerificationPhoneDisabledException : DomainException
{
  public CustomerVerificationPhoneDisabledException()
    : base(
        "Weryfikacja kodem na numer telefonu jest tymczasowo niedostępna. Wybierz weryfikację e-mail.",
        ErrorCodes.CustomerVerificationPhoneDisabled)
  {
  }
}
