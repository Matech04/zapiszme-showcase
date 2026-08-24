using App.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace App.Infrastructure.Email;

/// <summary>
/// In-memory throttle wysyłki maili reset-hasła per adres email.
/// Implementacja jest identyczna co do mechaniki z <c>BookingOtpProtectionService</c>
/// (single-host MemoryCache). Dla deploymentów multi-instance trzeba podmienić na
/// implementację rozproszoną (Redis itp.).
/// </summary>
public sealed class PasswordResetEmailThrottle : IPasswordResetEmailThrottle
{
  public static readonly TimeSpan CooldownWindow = TimeSpan.FromSeconds(60);

  private readonly IMemoryCache _cache;

  public PasswordResetEmailThrottle(IMemoryCache cache)
  {
    _cache = cache;
  }

  public bool TryAcquire(string email)
  {
    if (string.IsNullOrWhiteSpace(email))
    {
      return false;
    }

    var key = Key(email);
    if (_cache.TryGetValue(key, out _))
    {
      return false;
    }

    _cache.Set(
        key,
        true,
        new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CooldownWindow });
    return true;
  }

  private static string Key(string email) =>
      $"pwreset:throttle:{email.Trim().ToLowerInvariant()}";
}
