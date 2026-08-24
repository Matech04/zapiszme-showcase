using App.Domain.Common;

namespace App.Domain.Aggregates.UserAggregate;

/// <summary>
/// Jednorazowy 6-cyfrowy kod SMS używany do potwierdzenia numeru telefonu właściciela
/// po rejestracji. W danym czasie max 1 aktywny OTP per <see cref="UserId"/> (resend
/// invaliduje poprzedni przez <see cref="Consume"/>). Po przekroczeniu
/// <see cref="MaxAttempts"/> kod jest „locked" — wymaga nowego resendu.
/// </summary>
public class PhoneConfirmationOtp : Entity, IAggregateRoot
{
  public const int MaxAttempts = 5;

  public Guid UserId { get; private set; }
  public string CodeHash { get; private set; } = string.Empty;
  public DateTime CreatedAt { get; private set; }
  public DateTime ExpiresAt { get; private set; }
  public int AttemptsCount { get; private set; }
  public DateTime? ConsumedAt { get; private set; }

  private PhoneConfirmationOtp() { }

  public static PhoneConfirmationOtp Create(Guid userId, string codeHash, DateTime utcNow, TimeSpan ttl)
  {
    if (userId == Guid.Empty)
    {
      throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
    }
    if (string.IsNullOrWhiteSpace(codeHash))
    {
      throw new ArgumentException("CodeHash jest wymagany.", nameof(codeHash));
    }
    if (ttl <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(ttl), "TTL musi być dodatnie.");
    }

    return new PhoneConfirmationOtp
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      CodeHash = codeHash,
      CreatedAt = utcNow,
      ExpiresAt = utcNow + ttl,
      AttemptsCount = 0,
      ConsumedAt = null,
    };
  }

  public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;

  public bool IsLocked => AttemptsCount >= MaxAttempts;

  public bool IsActive(DateTime utcNow) => ConsumedAt is null && !IsExpired(utcNow) && !IsLocked;

  /// <summary>Rejestruje nieudaną próbę. Po <see cref="MaxAttempts"/> kod jest zablokowany.</summary>
  public void RegisterFailedAttempt() => AttemptsCount++;

  /// <summary>Konsumuje OTP — od tej chwili nie nadaje się do dalszej weryfikacji.</summary>
  public void Consume(DateTime utcNow) => ConsumedAt = utcNow;
}
