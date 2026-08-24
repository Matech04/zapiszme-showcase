using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Domain.UnitTests;

public sealed class OtpVerificationTests
{
  [Fact]
  public void IsValid_with_matching_code_before_expiry_returns_true()
  {
    var otp = OtpVerification.ForEmail("User@Example.com", "123456", DateTime.UtcNow.AddMinutes(5));

    Assert.True(otp.IsValid("123456", DateTime.UtcNow));
    Assert.Equal("user@example.com", otp.Email);
  }

  [Fact]
  public void IsValid_with_wrong_code_returns_false()
  {
    var otp = OtpVerification.ForEmail("a@b.co", "111111", DateTime.UtcNow.AddMinutes(5));

    Assert.False(otp.IsValid("999999", DateTime.UtcNow));
  }

  [Fact]
  public void IsValid_after_expiry_returns_false()
  {
    var expiry = DateTime.UtcNow.AddMinutes(-1);
    var otp = OtpVerification.ForEmail("a@b.co", "123456", expiry);

    Assert.False(otp.IsValid("123456", DateTime.UtcNow));
  }

  [Fact]
  public void ForPhone_persists_e164()
  {
    var otp = OtpVerification.ForPhone(new PhoneNumber("+48501234567"), "654321", DateTime.UtcNow.AddMinutes(5));

    Assert.Equal(OtpVerificationChannel.Phone, otp.Channel);
    Assert.Equal("+48501234567", otp.PhoneE164);
    Assert.True(otp.IsValid("654321", DateTime.UtcNow));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("not-an-email")]
  public void ForEmail_invalid_throws(string email)
  {
    Assert.Throws<ArgumentException>(() =>
        OtpVerification.ForEmail(email, "123456", DateTime.UtcNow.AddMinutes(5)));
  }
}
