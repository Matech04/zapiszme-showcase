using App.Infrastructure.Notifications.Sms;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>SMS-PHONE-* — normalizacja numerów do formatu wymaganego przez smsapi.pl.</summary>
public sealed class PhoneNumberNormalizerTests
{
  [Theory]
  [InlineData("501234567", "48501234567")]
  [InlineData("48501234567", "48501234567")]
  [InlineData("+48501234567", "48501234567")]
  [InlineData("+48 501 234 567", "48501234567")]
  [InlineData("501-234-567", "48501234567")]
  [InlineData("(48) 501 234 567", "48501234567")]
  public void ToSmsApiFormat_ValidPlNumbers_ReturnsCanonicalForm(string input, string expected)
  {
    Assert.Equal(expected, PhoneNumberNormalizer.ToSmsApiFormat(input));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("123")]                 // za krótki
  [InlineData("123456789012345")]     // za długi
  [InlineData("49123456789")]         // 11 cyfr, ale nie 48...
  public void ToSmsApiFormat_InvalidNumbers_Throws(string? input)
  {
    Assert.Throws<ArgumentException>(() => PhoneNumberNormalizer.ToSmsApiFormat(input));
  }

  [Fact]
  public void TryNormalize_InvalidInput_ReturnsNull()
  {
    Assert.Null(PhoneNumberNormalizer.TryNormalize("abc"));
  }

  [Fact]
  public void TryNormalize_ValidInput_ReturnsNormalized()
  {
    Assert.Equal("48501234567", PhoneNumberNormalizer.TryNormalize("+48 501 234 567"));
  }
}
