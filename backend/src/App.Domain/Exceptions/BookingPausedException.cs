namespace App.Domain.Exceptions;

/// <summary>
/// Salon wstrzymał rezerwacje (<c>Tenant.BookingPaused</c>) — publiczna rezerwacja online
/// jest tymczasowo wyłączona. Rzucany w write-flow publicznego bookingu (hold), żeby spreparowany
/// klient nie utworzył rezerwacji ani nie wymusił wysyłki OTP-SMS z pominięciem read-query, które
/// zwraca <c>IsBookingPaused = true</c>.
/// </summary>
public sealed class BookingPausedException : DomainException
{
  public BookingPausedException()
      : base(
          "Rezerwacje w tym salonie są chwilowo wstrzymane. Aby umówić wizytę, skontaktuj się z salonem bezpośrednio.",
          ErrorCodes.BookingPaused)
  {
  }
}
