namespace App.Domain.Exceptions;

public sealed class InvalidAppointmentStatusException : DomainException
{
  public InvalidAppointmentStatusException(int id)
      : base($"Status wizyty o id {id} jest nieprawidłowy.", ErrorCodes.AppointmentInvalidStatus)
  {
  }

  public InvalidAppointmentStatusException(string name)
      : base($"Status wizyty \"{name}\" jest nieprawidłowy.", ErrorCodes.AppointmentInvalidStatus)
  {
  }
}
