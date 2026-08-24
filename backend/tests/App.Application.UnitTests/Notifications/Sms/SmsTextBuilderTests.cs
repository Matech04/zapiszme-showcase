using App.Application.Notifications;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications.Sms;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>SMS-TEXT-* — szablony SMS dla każdego <see cref="NotificationType"/>.</summary>
public sealed class SmsTextBuilderTests
{
  [Fact]
  public void Build_BookingConfirmation_ContainsSalonAndDateTimeAndService()
  {
    var text = SmsTextBuilder.Build(
      NotificationType.BookingConfirmationToCustomer,
      Payload(salon: "Studio Ann", service: "Manicure", date: new DateOnly(2026, 6, 1), time: new TimeOnly(10, 30)));

    Assert.Contains("Studio Ann", text);
    Assert.Contains("01.06", text);
    Assert.Contains("10:30", text);
    Assert.Contains("Manicure", text);
  }

  [Fact]
  public void Build_Reschedule_ContainsOldAndNewTime()
  {
    var text = SmsTextBuilder.Build(
      NotificationType.RescheduleToCustomer,
      Payload(
        salon: "Salon",
        service: "Ciecie",
        date: new DateOnly(2026, 6, 2),
        time: new TimeOnly(14, 0),
        oldDate: new DateOnly(2026, 6, 1),
        oldTime: new TimeOnly(10, 0)));

    Assert.Contains("01.06", text);
    Assert.Contains("10:00", text);
    Assert.Contains("02.06", text);
    Assert.Contains("14:00", text);
  }

  [Fact]
  public void Build_AlwaysTransliteratesPolishChars()
  {
    var text = SmsTextBuilder.Build(
      NotificationType.BookingConfirmationToCustomer,
      Payload(salon: "Łódź Studio", service: "Cięcie", date: new DateOnly(2026, 6, 1), time: new TimeOnly(10, 0)));

    Assert.Contains("Lodz Studio", text);
    Assert.True(IsAscii(text), $"SMS musi być czystym ASCII, a jest: {text}");
  }

  [Theory]
  [InlineData(NotificationType.NewBookingToSalon)]
  [InlineData(NotificationType.BookingConfirmationToCustomer)]
  [InlineData(NotificationType.CancellationToSalon)]
  [InlineData(NotificationType.CancellationToCustomer)]
  [InlineData(NotificationType.RescheduleToSalon)]
  [InlineData(NotificationType.RescheduleToCustomer)]
  [InlineData(NotificationType.AppointmentReminderToCustomer)]
  [InlineData(NotificationType.AwaitingConfirmationToSalon)]
  [InlineData(NotificationType.CancelledBySalonToCustomer)]
  [InlineData(NotificationType.RescheduledBySalonToCustomer)]
  [InlineData(NotificationType.AppointmentReminder2hToCustomer)]
  public void Build_AllTypes_AreAsciiOnly(NotificationType type)
  {
    // Payload z polskimi znakami i myślnikiem w każdym polu zmiennym.
    var text = SmsTextBuilder.Build(type, Payload(
      salon: "Piękność—Łódź",
      customer: "Zażółć Gęślą",
      service: "Cięcie—stylizacja"));

    Assert.True(text.Length > 0);
    Assert.True(IsAscii(text), $"SMS dla {type} nie jest ASCII: {text}");
  }

  [Theory]
  [InlineData(NotificationType.NewBookingToSalon)]
  [InlineData(NotificationType.BookingConfirmationToCustomer)]
  [InlineData(NotificationType.CancellationToSalon)]
  [InlineData(NotificationType.CancellationToCustomer)]
  [InlineData(NotificationType.RescheduleToSalon)]
  [InlineData(NotificationType.RescheduleToCustomer)]
  [InlineData(NotificationType.AppointmentReminderToCustomer)]
  [InlineData(NotificationType.AwaitingConfirmationToSalon)]
  [InlineData(NotificationType.CancelledBySalonToCustomer)]
  [InlineData(NotificationType.RescheduledBySalonToCustomer)]
  [InlineData(NotificationType.AppointmentReminder2hToCustomer)]
  public void Build_LongInputs_StayWithinMaxLength(NotificationType type)
  {
    // Skrajnie długie pola zmienne — wynik nadal musi zmieścić się w 1 segmencie.
    var text = SmsTextBuilder.Build(type, Payload(
      salon: new string('S', 80),
      customer: new string('K', 80),
      service: new string('U', 80)));

    Assert.True(
      text.Length <= SmsTextBuilder.MaxLength,
      $"SMS dla {type} ma {text.Length} znaków (limit {SmsTextBuilder.MaxLength}).");
  }

  [Fact]
  public void Build_NormalInputs_ContainSalonName()
  {
    var text = SmsTextBuilder.Build(NotificationType.AppointmentReminderToCustomer, Payload(salon: "MySalon"));
    Assert.Contains("MySalon", text);
  }

  private static bool IsAscii(string s)
  {
    foreach (var ch in s)
    {
      if (ch > 127)
      {
        return false;
      }
    }
    return true;
  }

  private static NotificationPayload Payload(
    string salon = "Salon",
    string customer = "Jan",
    string service = "Usluga",
    DateOnly? date = null,
    TimeOnly? time = null,
    DateOnly? oldDate = null,
    TimeOnly? oldTime = null) =>
    new(
      SalonName: salon,
      CustomerName: customer,
      StaffName: "Ann",
      ServiceName: service,
      Date: date ?? new DateOnly(2026, 6, 1),
      StartTime: time ?? new TimeOnly(10, 0),
      OldDate: oldDate,
      OldStartTime: oldTime);
}
