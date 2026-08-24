using App.Infrastructure.Booking;
using App.Domain.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// HOLD-CAP-* — per-IP cap jednoczesnych holdów był check-then-set bez sekcji krytycznej. N równoległych
/// żądań /hold z jednego IP czytało ten sam licznik zanim którekolwiek go podbiło (TOCTOU) i wszystkie
/// przechodziły próg. Preflight 2026-07-09, MEDIUM: bez tego cap 5 nie rekompensuje długiego HoldTtl.
/// </summary>
public sealed class HoldCapConcurrencyTests
{
  private const int Cap = 5;

  private static BookingOtpProtectionService CreateService() =>
    new(
      new MemoryCache(new MemoryCacheOptions()),
      TimeProvider.System,
      Options.Create(new BookingOtpProtectionOptions { MaxConcurrentHoldsPerIp = Cap }));

  [Fact]
  public void RegisterHoldCreatedForIp_UnderConcurrency_NeverExceedsCap()
  {
    var service = CreateService();
    const string ip = "203.0.113.7";
    const int attempts = 64;

    var accepted = 0;
    Parallel.For(0, attempts, _ =>
    {
      try
      {
        service.RegisterHoldCreatedForIp(ip);
        Interlocked.Increment(ref accepted);
      }
      catch (RateLimitExceededException)
      {
        // oczekiwane po wyczerpaniu capa
      }
    });

    Assert.Equal(Cap, accepted);
  }

  [Fact]
  public void ReleaseHoldForIp_UnderConcurrency_DoesNotLoseCounts()
  {
    var service = CreateService();
    const string ip = "203.0.113.8";

    for (var i = 0; i < Cap; i++)
    {
      service.RegisterHoldCreatedForIp(ip);
    }

    // Zwolnienie wszystkich holdów musi w pełni odblokować IP — nie mniej, nie więcej.
    Parallel.For(0, Cap, _ => service.ReleaseHoldForIp(ip));

    var acceptedAfterRelease = 0;
    for (var i = 0; i < Cap; i++)
    {
      service.RegisterHoldCreatedForIp(ip);
      acceptedAfterRelease++;
    }

    Assert.Equal(Cap, acceptedAfterRelease);
    Assert.Throws<RateLimitExceededException>(() => service.RegisterHoldCreatedForIp(ip));
  }

  [Fact]
  public void RegisterHoldCreatedForIp_CapDisabled_NeverThrows()
  {
    var service = new BookingOtpProtectionService(
      new MemoryCache(new MemoryCacheOptions()),
      TimeProvider.System,
      Options.Create(new BookingOtpProtectionOptions { MaxConcurrentHoldsPerIp = 0 }));

    for (var i = 0; i < 20; i++)
    {
      service.RegisterHoldCreatedForIp("203.0.113.9");
    }
  }
}
