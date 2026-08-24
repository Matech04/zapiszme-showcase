namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Preferencje powiadomień salonu — jeden przełącznik (on/off) na każdy <see cref="NotificationType"/>.
/// Kanał nie jest tu wybierany: powiadomienia DO KLIENTA idą kanałem komunikacji z klientem
/// (<see cref="Tenant.CustomerVerificationChannel"/> — SMS albo e-mail, czyli jedyny kontakt jaki
/// zbieramy przy rezerwacji), a powiadomienia DO SALONU e-mailem + w panelu (in-app).
/// Owned type na <see cref="Tenant"/>. Domyślnie wszystkie włączone.
/// </summary>
public class NotificationSettings
{
  public bool NewBookingToSalon { get; private set; } = true;
  public bool BookingConfirmationToCustomer { get; private set; } = true;
  public bool CancellationToSalon { get; private set; } = true;
  public bool CancellationToCustomer { get; private set; } = true;
  public bool RescheduleToSalon { get; private set; } = true;
  public bool RescheduleToCustomer { get; private set; } = true;
  public bool AppointmentReminderToCustomer { get; private set; } = true;
  public bool AwaitingConfirmationToSalon { get; private set; } = true;
  public bool CancelledBySalonToCustomer { get; private set; } = true;
  public bool RescheduledBySalonToCustomer { get; private set; } = true;
  public bool AppointmentReminder2hToCustomer { get; private set; } = true;
  public bool StaffBookedAppointmentToCustomer { get; private set; } = true;

  private NotificationSettings() { }

  public NotificationSettings(
    bool newBookingToSalon,
    bool bookingConfirmationToCustomer,
    bool cancellationToSalon,
    bool cancellationToCustomer,
    bool rescheduleToSalon,
    bool rescheduleToCustomer,
    bool appointmentReminderToCustomer,
    bool awaitingConfirmationToSalon,
    bool cancelledBySalonToCustomer,
    bool rescheduledBySalonToCustomer,
    bool appointmentReminder2hToCustomer,
    bool staffBookedAppointmentToCustomer)
  {
    NewBookingToSalon = newBookingToSalon;
    BookingConfirmationToCustomer = bookingConfirmationToCustomer;
    CancellationToSalon = cancellationToSalon;
    CancellationToCustomer = cancellationToCustomer;
    RescheduleToSalon = rescheduleToSalon;
    RescheduleToCustomer = rescheduleToCustomer;
    AppointmentReminderToCustomer = appointmentReminderToCustomer;
    AwaitingConfirmationToSalon = awaitingConfirmationToSalon;
    CancelledBySalonToCustomer = cancelledBySalonToCustomer;
    RescheduledBySalonToCustomer = rescheduledBySalonToCustomer;
    AppointmentReminder2hToCustomer = appointmentReminder2hToCustomer;
    StaffBookedAppointmentToCustomer = staffBookedAppointmentToCustomer;
  }

  public static NotificationSettings AllEnabled() =>
    new(true, true, true, true, true, true, true, true, true, true, true, true);

  /// <summary>
  /// Domyślne ustawienia powiadomień dla nowo zakładanego salonu. Względem <see cref="AllEnabled"/>
  /// wyłączone są: potwierdzenie rezerwacji do klienta i przypomnienie 2h przed wizytą — pozostałe
  /// (m.in. przypomnienie 24h i potwierdzenie wizyty wystawionej ręcznie przez salon) włączone.
  /// </summary>
  public static NotificationSettings Defaults() =>
    new(
      newBookingToSalon: true,
      bookingConfirmationToCustomer: false,
      cancellationToSalon: true,
      cancellationToCustomer: true,
      rescheduleToSalon: true,
      rescheduleToCustomer: true,
      appointmentReminderToCustomer: true,
      awaitingConfirmationToSalon: true,
      cancelledBySalonToCustomer: true,
      rescheduledBySalonToCustomer: true,
      appointmentReminder2hToCustomer: false,
      staffBookedAppointmentToCustomer: true);

  public bool IsEnabled(NotificationType type) => type switch
  {
    NotificationType.NewBookingToSalon => NewBookingToSalon,
    NotificationType.BookingConfirmationToCustomer => BookingConfirmationToCustomer,
    NotificationType.CancellationToSalon => CancellationToSalon,
    NotificationType.CancellationToCustomer => CancellationToCustomer,
    NotificationType.RescheduleToSalon => RescheduleToSalon,
    NotificationType.RescheduleToCustomer => RescheduleToCustomer,
    NotificationType.AppointmentReminderToCustomer => AppointmentReminderToCustomer,
    NotificationType.AwaitingConfirmationToSalon => AwaitingConfirmationToSalon,
    NotificationType.CancelledBySalonToCustomer => CancelledBySalonToCustomer,
    NotificationType.RescheduledBySalonToCustomer => RescheduledBySalonToCustomer,
    NotificationType.AppointmentReminder2hToCustomer => AppointmentReminder2hToCustomer,
    NotificationType.StaffBookedAppointmentToCustomer => StaffBookedAppointmentToCustomer,
    _ => false,
  };
}
