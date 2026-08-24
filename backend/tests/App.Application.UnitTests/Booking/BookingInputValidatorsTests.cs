using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Common.Validation;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// BOOKING-VALIDATION-* — publiczne, anonimowe commandy nie miały żadnej walidacji pól klienta.
/// Regresja z preflightu: imię „[%idzdo:evil.pl%]" trafiało do treści SMS-a wysyłanego do salonu.
/// Po deduplikacji walidatorów (preflight 2026-07-15) kanonicznym źródłem reguł RequestOtp/Create/Update
/// jest App.Application.Common.Validation (dodatkowo wymusza format telefonu i obecność kontaktu);
/// reguły charset/makro pozostają. ConfirmBookingWithSession żyje dalej w namespace bookingu.
/// </summary>
public sealed class BookingInputValidatorsTests
{
  private static RequestOtpCommand Otp(string? firstName = "Anna", string? email = null) =>
    new(Guid.NewGuid(), Guid.NewGuid(), "+48501234567", email, firstName, "Kowalska");

  [Theory]
  [InlineData("Anna")]
  [InlineData("Zofia Łąka-Żółć")]      // polskie znaki + myślnik
  [InlineData("O'Brien")]              // apostrof
  [InlineData(null)]                   // pole opcjonalne
  [InlineData("")]
  public void RequestOtp_AcceptsRealisticNames(string? firstName)
  {
    var result = new RequestOtpCommandValidator().Validate(Otp(firstName));
    Assert.True(result.IsValid);
  }

  [Theory]
  [InlineData("[%idzdo:evil.pl%]")]    // makro smsapi
  [InlineData("Anna %] Nowak")]
  [InlineData("<script>alert(1)</script>")]
  public void RequestOtp_RejectsNamesWithMacroOrMarkup(string firstName)
  {
    var result = new RequestOtpCommandValidator().Validate(Otp(firstName));
    Assert.False(result.IsValid);
  }

  [Fact]
  public void RequestOtp_RejectsOverlongName()
  {
    // Kanoniczny walidator (ValidationLimits.Name = 100) — powyżej limitu odrzuca.
    var result = new RequestOtpCommandValidator().Validate(Otp(new string('a', 101)));
    Assert.False(result.IsValid);
  }

  [Fact]
  public void RequestOtp_RejectsMalformedEmail()
  {
    var result = new RequestOtpCommandValidator().Validate(Otp(email: "nie-mail"));
    Assert.False(result.IsValid);
  }

  [Fact]
  public void RequestOtp_AcceptsMissingEmail()
  {
    var result = new RequestOtpCommandValidator().Validate(Otp(email: null));
    Assert.True(result.IsValid);
  }

  [Theory]
  [InlineData("anna_kowalska")]
  [InlineData("@anna.k")]
  [InlineData(null)]
  public void ConfirmWithSession_AcceptsInstagramNick(string? nick)
  {
    var cmd = new ConfirmBookingWithSessionCommand(
      "salon", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Anna", "Kowalska", nick);

    Assert.True(new ConfirmBookingWithSessionCommandValidator().Validate(cmd).IsValid);
  }

  [Fact]
  public void ConfirmWithSession_RejectsInstagramNickWithMacroChars()
  {
    var cmd = new ConfirmBookingWithSessionCommand(
      "salon", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Anna", "Kowalska", "[%idzdo:x%]");

    Assert.False(new ConfirmBookingWithSessionCommandValidator().Validate(cmd).IsValid);
  }

  [Fact]
  public void CreateBookingAppointment_RejectsEmptyServiceList()
  {
    var cmd = new CreateBookingAppointmentCommand(Guid.NewGuid(), [], new DateOnly(2026, 12, 1), new TimeOnly(10, 0));
    Assert.False(new CreateBookingAppointmentCommandValidator().Validate(cmd).IsValid);
  }

  [Fact]
  public void CreateBookingAppointment_AcceptsSingleService()
  {
    var cmd = new CreateBookingAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()], new DateOnly(2026, 12, 1), new TimeOnly(10, 0));
    Assert.True(new CreateBookingAppointmentCommandValidator().Validate(cmd).IsValid);
  }

  [Fact]
  public void UpdatePublicAppointment_RejectsEmptyToken()
  {
    var cmd = new UpdatePublicAppointmentCommand(
      Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), [Guid.NewGuid()], new DateOnly(2026, 12, 1), new TimeOnly(10, 0));
    Assert.False(new UpdatePublicAppointmentCommandValidator().Validate(cmd).IsValid);
  }
}
