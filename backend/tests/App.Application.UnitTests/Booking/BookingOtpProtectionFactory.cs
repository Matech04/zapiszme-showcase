using App.Infrastructure.Booking;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// Test-only fabryka <see cref="BookingOtpProtectionService"/> — owija opcje w IOptions, żeby testy
/// nie musiały tego robić ręcznie (serwis ma JEDEN konstruktor DI z IOptions).
/// </summary>
internal static class BookingOtpProtectionFactory
{
  public static BookingOtpProtectionService Create(
      IMemoryCache cache,
      TimeProvider timeProvider,
      BookingOtpProtectionOptions? options = null) =>
      new(cache, timeProvider, Options.Create(options ?? new BookingOtpProtectionOptions()));
}
