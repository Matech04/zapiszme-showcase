using App.Domain.Common;

namespace App.Domain.Aggregates.SelfServiceAggregate;

/// <summary>
/// Jednorazowy kod OTP w trybie samoobsługi klienta (anulowanie/przełożenie wizyty).
/// Niezwiązany z agregatem `Appointment` — kontakt (email/phone) identyfikuje klienta
/// po stronie tenanta. Kod jest hashowany; po weryfikacji wystawiamy krótkotrwały JWT.
/// </summary>
public class SelfServiceOtp : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }

  /// <summary>Znormalizowany email (lowercase) lub null, gdy używamy telefonu.</summary>
  public string? Email { get; private set; }

  /// <summary>Numer telefonu w E.164 lub null, gdy używamy emaila.</summary>
  public string? PhoneE164 { get; private set; }

  /// <summary>Hash kodu (SHA-256, hex). Sam kod nie jest przechowywany.</summary>
  public string CodeHash { get; private set; } = string.Empty;

  public DateTime CreatedAtUtc { get; private set; }
  public DateTime ExpiresAtUtc { get; private set; }

  /// <summary>Liczba dotychczasowych nieudanych prób weryfikacji (limit 3).</summary>
  public int FailedAttempts { get; private set; }

  /// <summary>Pierwszy `VerifyOtp` z poprawnym kodem ustawia ten znacznik. Zapobiega ponownemu użyciu.</summary>
  public bool Consumed { get; private set; }

  /// <summary>Token sesji wystawiony po pomyślnej weryfikacji (opaque Bearer w nagłówku Authorization).</summary>
  public Guid? SessionToken { get; private set; }

  /// <summary>Wygasanie tokenu sesji (UTC). Domyślnie 30 minut po consume().</summary>
  public DateTime? SessionExpiresAtUtc { get; private set; }

  private SelfServiceOtp() { }

  private SelfServiceOtp(
    Guid id,
    Guid tenantId,
    string? email,
    string? phoneE164,
    string codeHash,
    DateTime createdAtUtc,
    DateTime expiresAtUtc)
  {
    Id = id;
    TenantId = tenantId;
    Email = email;
    PhoneE164 = phoneE164;
    CodeHash = codeHash;
    CreatedAtUtc = createdAtUtc;
    ExpiresAtUtc = expiresAtUtc;
  }

  public static SelfServiceOtp ForEmail(Guid tenantId, string email, string codeHash, DateTime nowUtc, TimeSpan ttl)
  {
    var normalized = email.Trim().ToLowerInvariant();
    return new SelfServiceOtp(Guid.NewGuid(), tenantId, normalized, null, codeHash, nowUtc, nowUtc.Add(ttl));
  }

  public static SelfServiceOtp ForPhone(Guid tenantId, string phoneE164, string codeHash, DateTime nowUtc, TimeSpan ttl)
  {
    return new SelfServiceOtp(Guid.NewGuid(), tenantId, null, phoneE164, codeHash, nowUtc, nowUtc.Add(ttl));
  }

  /// <summary>
  /// Tworzy wiersz reprezentujący sesję zweryfikowanego kontaktu BEZ wyzwania kodem — używane,
  /// gdy własność kontaktu została już udowodniona innym kanałem (np. OTP przy rezerwacji). Wiersz
  /// rodzi się <see cref="Consumed"/> z gotowym <see cref="SessionToken"/>; <see cref="CodeHash"/>
  /// jest pusty (nie da się go „skonsumować” ponownie). <paramref name="codeRetention"/> ustawia
  /// <see cref="ExpiresAtUtc"/> tak, by sprzątanie po wieku kodu działało bez zmian (sesja żyje wg
  /// <see cref="SessionExpiresAtUtc"/>). Dokładnie jeden z email/phone musi być podany.
  /// </summary>
  public static SelfServiceOtp CreateVerifiedSession(
    Guid tenantId,
    string? email,
    string? phoneE164,
    DateTime nowUtc,
    TimeSpan codeRetention,
    TimeSpan sessionLifetime)
  {
    var normalizedEmail = email?.Trim().ToLowerInvariant();
    var otp = new SelfServiceOtp(
      Guid.NewGuid(),
      tenantId,
      normalizedEmail,
      phoneE164,
      string.Empty,
      nowUtc,
      nowUtc.Add(codeRetention))
    {
      Consumed = true,
      SessionToken = Guid.NewGuid(),
      SessionExpiresAtUtc = nowUtc.Add(sessionLifetime),
    };
    return otp;
  }

  public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
  public bool IsBlocked => FailedAttempts >= 3 || Consumed;

  public bool TryConsume(string codeHash, DateTime nowUtc, TimeSpan sessionLifetime)
  {
    if (IsExpired(nowUtc) || Consumed)
    {
      return false;
    }

    if (!string.Equals(CodeHash, codeHash, StringComparison.Ordinal))
    {
      FailedAttempts++;
      return false;
    }

    Consumed = true;
    SessionToken = Guid.NewGuid();
    SessionExpiresAtUtc = nowUtc.Add(sessionLifetime);
    return true;
  }

  public bool IsSessionValid(Guid token, DateTime nowUtc)
  {
    return Consumed
        && SessionToken.HasValue
        && SessionToken.Value == token
        && SessionExpiresAtUtc.HasValue
        && nowUtc < SessionExpiresAtUtc.Value;
  }
}
