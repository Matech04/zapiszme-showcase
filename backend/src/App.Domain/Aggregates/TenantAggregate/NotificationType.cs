namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Rodzaj powiadomienia w granularności (zdarzenie biznesowe + odbiorca). Salon włącza/wyłącza
/// każdy z osobna w <see cref="NotificationSettings"/>.
/// </summary>
public enum NotificationType
{
  /// <summary>Powiadomienie do salonu o nowej rezerwacji online (po weryfikacji OTP).</summary>
  NewBookingToSalon = 1,

  /// <summary>Potwierdzenie rezerwacji wysyłane do klienta.</summary>
  BookingConfirmationToCustomer = 2,

  /// <summary>Powiadomienie do salonu o anulowaniu wizyty przez klienta.</summary>
  CancellationToSalon = 3,

  /// <summary>Potwierdzenie anulowania wysyłane do klienta.</summary>
  CancellationToCustomer = 4,

  /// <summary>Powiadomienie do salonu o przełożeniu wizyty przez klienta.</summary>
  RescheduleToSalon = 5,

  /// <summary>Potwierdzenie przełożenia wysyłane do klienta.</summary>
  RescheduleToCustomer = 6,

  /// <summary>Przypomnienie 24h przed wizytą wysyłane do klienta.</summary>
  AppointmentReminderToCustomer = 7,

  /// <summary>Powiadomienie do salonu, że rezerwacja czeka na ręczne potwierdzenie (tryb Manual).</summary>
  AwaitingConfirmationToSalon = 8,

  /// <summary>Powiadomienie do klienta, że salon odwołał/odrzucił jego wizytę.</summary>
  CancelledBySalonToCustomer = 9,

  /// <summary>Powiadomienie do klienta, że salon przełożył jego wizytę.</summary>
  RescheduledBySalonToCustomer = 10,

  /// <summary>Przypomnienie ~2h przed wizytą wysyłane do klienta.</summary>
  AppointmentReminder2hToCustomer = 11,

  /// <summary>
  /// Kod weryfikacyjny (OTP) wysyłany do klienta przy rezerwacji online. Nie jest sterowany
  /// <see cref="NotificationSettings"/> (zawsze wymagany w flow), ale liczy się do zużycia SMS/e-mail.
  /// </summary>
  CustomerVerificationOtp = 12,

  /// <summary>
  /// Potwierdzenie do klienta, że salon ręcznie wystawił mu wizytę w panelu (rezerwacja zrobiona
  /// przez pracownika, a nie przez klienta online).
  /// </summary>
  StaffBookedAppointmentToCustomer = 13,

  /// <summary>
  /// Link do zapłaty zadatku wysyłany do klienta na żądanie personelu (przycisk „Wyślij klientowi").
  /// Akcja jawna — nie jest sterowana <see cref="NotificationSettings"/>, ale liczy się do zużycia SMS/e-mail.
  /// </summary>
  DepositLinkToCustomer = 14,
}
