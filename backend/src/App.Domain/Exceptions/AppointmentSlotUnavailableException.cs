namespace App.Domain.Exceptions;

public sealed class AppointmentSlotUnavailableException : DomainException
{
  public AppointmentSlotUnavailableException()
      : base("Wybrany termin jest niedostępny albo został już zajęty.", ErrorCodes.AppointmentSlotUnavailable)
  {
  }
}
