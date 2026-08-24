using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Domain.Services;

/// <summary>
/// Reguły usuwania wizyt oczekujących z publicznej rezerwacji (HoldLease).
/// Wywołanie z zadania w tle — sama klasa nie dotyka bazy ani hosta.
/// </summary>
public static class AppointmentCleanupService
{
  /// <summary>
  /// Zwraca true, gdy wizytę Pending z publicznej rezerwacji należy usunąć z bazy — wygasła dzierżawa slotu
  /// (granica jak w <see cref="HoldLease.IsExpired"/>: po <see cref="HoldLease.ExpiryTimeUtc"/>).
  /// </summary>
  public static bool ShouldRemovePendingDueToExpiredHoldLease(Appointment appointment, DateTime utcNow)
  {
    var isHeldStatus = appointment.Status == AppointmentStatus.Pending
      || appointment.Status == AppointmentStatus.AwaitingOtp;

    if (!isHeldStatus || appointment.Lease is null)
    {
      return false;
    }

    return utcNow > appointment.Lease.ExpiryTimeUtc;
  }

  /// <summary>
  /// Czy wizytę można TRWALE usunąć (hard-delete) w trybie „nie przechowuj historii wizyt". Reguła
  /// celowo konserwatywna — usuwamy WYŁĄCZNIE wizyty, które są jednocześnie:
  /// (1) w stanie TERMINALNYM (<see cref="AppointmentStatus.Completed"/> lub
  /// <see cref="AppointmentStatus.Canceled"/>) — nigdy bieżące/oczekujące/aktywne holdy,
  /// (2) z datą w PRZESZŁOŚCI (<paramref name="appointmentDate"/> &lt; <paramref name="todayInBusinessTz"/>) —
  /// nigdy dzisiejsze ani przyszłe.
  /// „Dziś" wyznacza wołający wg strefy czasowej salonu (<see cref="TenantAggregate.Tenant.TimeZoneId"/>).
  /// Pending / AwaitingOtp / Booked / InProgress NIE są tu objęte — anti-abuse holdów i obsługa wizyt zostają nietknięte.
  /// </summary>
  public static bool ShouldPurgeForNoHistoryRetention(
    AppointmentStatus status,
    DateOnly appointmentDate,
    DateOnly todayInBusinessTz)
  {
    var isTerminal = status == AppointmentStatus.Completed
      || status == AppointmentStatus.Canceled;

    return isTerminal && appointmentDate < todayInBusinessTz;
  }
}
