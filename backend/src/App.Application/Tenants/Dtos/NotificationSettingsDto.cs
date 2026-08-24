namespace App.Application.Tenants.Dtos;

/// <summary>
/// Preferencje powiadomień salonu — jeden przełącznik (on/off) na każdy typ powiadomienia.
/// Kanał nie jest wybierany: powiadomienia do klienta idą kanałem komunikacji z klientem
/// (<c>CustomerVerificationChannel</c>), a do salonu e-mailem + w panelu.
/// </summary>
public record NotificationSettingsDto(
  bool NewBookingToSalon,
  bool BookingConfirmationToCustomer,
  bool CancellationToSalon,
  bool CancellationToCustomer,
  bool RescheduleToSalon,
  bool RescheduleToCustomer,
  bool AppointmentReminderToCustomer,
  bool AwaitingConfirmationToSalon,
  bool CancelledBySalonToCustomer,
  bool RescheduledBySalonToCustomer,
  bool AppointmentReminder2hToCustomer,
  bool StaffBookedAppointmentToCustomer);
