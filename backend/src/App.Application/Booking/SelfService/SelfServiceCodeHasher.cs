using App.Application.Common.Security;

namespace App.Application.Booking.SelfService;

/// <summary>Wrapper na <see cref="OtpCodeHasher"/> — zachowuje istniejący API booking flow.</summary>
internal static class SelfServiceCodeHasher
{
  public static string Hash(string code) => OtpCodeHasher.Hash(code);
}
