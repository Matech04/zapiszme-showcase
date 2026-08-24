namespace App.Domain.Exceptions;

/// <summary>
/// Salon nie przyjmuje publicznych rezerwacji, bo jego subskrypcja nie jest aktywna
/// (EffectiveStatus poza {Trial, Active} — np. PastDue / Canceled / wygasły trial).
/// Rzucany w write-flow publicznego bookingu (hold + request-otp), żeby spreparowany klient
/// nie utworzył fałszywej rezerwacji ani nie wymusił wysyłki (drenażu) OTP-SMS u nieaktywnego salonu.
/// </summary>
public sealed class BookingUnavailableException : DomainException
{
  public BookingUnavailableException()
      : base(
          "Ten salon nie przyjmuje obecnie rezerwacji online. Skontaktuj się z salonem bezpośrednio.",
          ErrorCodes.BookingUnavailable)
  {
  }
}
