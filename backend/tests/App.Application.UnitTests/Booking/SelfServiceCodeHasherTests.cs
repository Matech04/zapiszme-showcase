using App.Application.Booking.SelfService;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// DOM-SS-003, DOM-SS-004 — SHA-256 hex hasher dla SelfServiceOtp.
/// </summary>
public sealed class SelfServiceCodeHasherTests
{
  [Fact]
  public void Hash_produces_sha256_hex_for_known_input()
  {
    // SHA-256("123456") = 8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92
    Assert.Equal(
      "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92",
      SelfServiceCodeHasher.Hash("123456"));
  }

  [Fact]
  public void Hash_is_deterministic_and_unique_per_input()
  {
    Assert.Equal(SelfServiceCodeHasher.Hash("000000"), SelfServiceCodeHasher.Hash("000000"));
    Assert.NotEqual(SelfServiceCodeHasher.Hash("000000"), SelfServiceCodeHasher.Hash("999999"));
  }
}
