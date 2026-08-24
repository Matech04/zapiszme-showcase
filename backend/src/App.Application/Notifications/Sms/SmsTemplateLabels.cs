using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>Czytelne PL etykiety personalizowalnych typów SMS — do listy w panelu salonu/admina.</summary>
public static class SmsTemplateLabels
{
  public static string For(NotificationType type) => type switch
  {
    NotificationType.BookingConfirmationToCustomer => "Potwierdzenie rezerwacji",
    NotificationType.AppointmentReminderToCustomer => "Przypomnienie (24h przed)",
    NotificationType.AppointmentReminder2hToCustomer => "Przypomnienie (2h przed)",
    NotificationType.CancellationToCustomer => "Potwierdzenie anulowania",
    NotificationType.RescheduleToCustomer => "Potwierdzenie przełożenia",
    NotificationType.CancelledBySalonToCustomer => "Odwołanie wizyty przez salon",
    NotificationType.RescheduledBySalonToCustomer => "Przełożenie wizyty przez salon",
    NotificationType.StaffBookedAppointmentToCustomer => "Wizyta wystawiona przez salon",
    NotificationType.DepositLinkToCustomer => "Link do zapłaty zadatku",
    _ => type.ToString(),
  };
}
