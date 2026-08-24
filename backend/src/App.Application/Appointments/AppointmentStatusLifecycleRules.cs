using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Application.Appointments;

/// <summary>
/// Reguły automatycznej zmiany statusów wizyt wg czasu salonu (strefa z konfiguracji tenanta).
/// </summary>
public static class AppointmentStatusLifecycleRules
{
  /// <summary>
  /// Wybiera nowy status albo null, jeśli brak automatycznej zmiany.
  /// Kolejność: najpierw zakończenie (end), potem start (Booked → InProgress).
  /// </summary>
  public static AppointmentStatus? ResolveTransition(
    AppointmentStatus current,
    DateTime startUtc,
    DateTime endUtc,
    DateTime utcNow)
  {
    if (current == AppointmentStatus.Canceled || current == AppointmentStatus.Completed)
    {
      return null;
    }

    if (utcNow >= endUtc)
    {
      if (current == AppointmentStatus.Pending || current == AppointmentStatus.AwaitingOtp)
      {
        return AppointmentStatus.Canceled;
      }

      if (current == AppointmentStatus.Booked || current == AppointmentStatus.InProgress)
      {
        return AppointmentStatus.Completed;
      }

      return null;
    }

    if (utcNow >= startUtc && current == AppointmentStatus.Booked)
    {
      return AppointmentStatus.InProgress;
    }

    return null;
  }
}
