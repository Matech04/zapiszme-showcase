using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

public class NotificationSettingsTests
{
  [Fact]
  public void AllEnabled_ShouldEnableEveryType()
  {
    var settings = NotificationSettings.AllEnabled();

    foreach (NotificationType type in Enum.GetValues<NotificationType>())
    {
      // Typy staff-triggered nie są przełącznikami ustawień — IsEnabled celowo zwraca dla nich
      // false: CustomerVerificationOtp (OTP zawsze wymagany w flow) oraz DepositLinkToCustomer
      // (jawna akcja personelu „Wyślij klientowi").
      if (type is NotificationType.CustomerVerificationOtp or NotificationType.DepositLinkToCustomer)
      {
        continue;
      }
      Assert.True(settings.IsEnabled(type), $"{type} powinno być włączone");
    }
  }

  [Fact]
  public void IsEnabled_ShouldReflectPerTypeFlags()
  {
    // newBooking=false, bookingConfirmation=true, cancellationToSalon=false,
    // cancellationToCustomer=true, rescheduleToSalon=false, rescheduleToCustomer=true,
    // reminder=false
    var settings = new NotificationSettings(
      newBookingToSalon: false,
      bookingConfirmationToCustomer: true,
      cancellationToSalon: false,
      cancellationToCustomer: true,
      rescheduleToSalon: false,
      rescheduleToCustomer: true,
      appointmentReminderToCustomer: false,
      awaitingConfirmationToSalon: true,
      cancelledBySalonToCustomer: false,
      rescheduledBySalonToCustomer: true,
      appointmentReminder2hToCustomer: false,
      staffBookedAppointmentToCustomer: true);

    Assert.False(settings.IsEnabled(NotificationType.NewBookingToSalon));
    Assert.True(settings.IsEnabled(NotificationType.BookingConfirmationToCustomer));
    Assert.False(settings.IsEnabled(NotificationType.CancellationToSalon));
    Assert.True(settings.IsEnabled(NotificationType.CancellationToCustomer));
    Assert.False(settings.IsEnabled(NotificationType.RescheduleToSalon));
    Assert.True(settings.IsEnabled(NotificationType.RescheduleToCustomer));
    Assert.False(settings.IsEnabled(NotificationType.AppointmentReminderToCustomer));
    Assert.True(settings.IsEnabled(NotificationType.AwaitingConfirmationToSalon));
    Assert.False(settings.IsEnabled(NotificationType.CancelledBySalonToCustomer));
    Assert.True(settings.IsEnabled(NotificationType.RescheduledBySalonToCustomer));
    Assert.False(settings.IsEnabled(NotificationType.AppointmentReminder2hToCustomer));
    Assert.True(settings.IsEnabled(NotificationType.StaffBookedAppointmentToCustomer));
  }

}
