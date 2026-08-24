namespace App.Domain.Aggregates.AppointmentAggregate;

/// <summary>
/// Stan płatności zadatku na wizycie — ortogonalny do <see cref="AppointmentStatus"/> (terminarz).
/// Wizyta może być Booked i jednocześnie AwaitingPayment / Paid / Refunded.
/// </summary>
public enum AppointmentPaymentStatus
{
  /// <summary>Zadatek nie jest wymagany / nigdy nie wygenerowano linku.</summary>
  NotRequired = 0,

  /// <summary>Link wygenerowany, oczekuje na opłacenie (do <see cref="Appointment.LinkExpiresAtUtc"/>).</summary>
  AwaitingPayment = 1,

  /// <summary>Zadatek opłacony — środki na koncie salonu u operatora.</summary>
  Paid = 2,

  /// <summary>Link wygasł bez opłaty. Stan liczony pochodnie nie jest persystowany — patrz <see cref="Appointment.IsDepositLinkExpired"/>.</summary>
  Expired = 3,

  /// <summary>Opłacony zadatek został zwrócony klientowi.</summary>
  Refunded = 4,
}
