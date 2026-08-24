namespace App.Domain.Exceptions;

/// <summary>
/// Salon ma włączony tryb „tylko zaproszeni” (whitelist) i kontakt rezerwującego nie znajduje się
/// na liście klientów `IsWhitelisted=true`.
/// </summary>
public sealed class BookingNotInvitedException : DomainException
{
  public BookingNotInvitedException()
      : base(
          "Ten salon przyjmuje rezerwacje wyłącznie z zaproszeniem. Skontaktuj się z salonem, aby otrzymać dostęp.",
          ErrorCodes.BookingNotInvited)
  {
  }
}
