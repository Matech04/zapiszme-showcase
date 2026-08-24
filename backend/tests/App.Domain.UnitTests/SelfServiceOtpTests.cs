using App.Domain.Aggregates.SelfServiceAggregate;

namespace App.Domain.UnitTests;

/// <summary>
/// DOM-SS-001..014 — testy domenowe SelfServiceOtp (TTL, hash, TryConsume, sesja, blokady).
/// </summary>
public sealed class SelfServiceOtpTests
{
  // DOM-SS-001
  [Fact]
  public void ForEmail_normalises_to_lowercase_and_sets_ttl()
  {
    var tenantId = Guid.NewGuid();
    var nowUtc = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
    var ttl = TimeSpan.FromMinutes(10);

    var otp = SelfServiceOtp.ForEmail(tenantId, "Guest@Example.COM", "hash-abc", nowUtc, ttl);

    Assert.Equal("guest@example.com", otp.Email);
    Assert.Null(otp.PhoneE164);
    Assert.Equal("hash-abc", otp.CodeHash);
    Assert.Equal(nowUtc, otp.CreatedAtUtc);
    Assert.Equal(nowUtc.Add(ttl), otp.ExpiresAtUtc);
  }

  // DOM-SS-002
  [Fact]
  public void ForPhone_keeps_e164_and_sets_ttl()
  {
    var tenantId = Guid.NewGuid();
    var nowUtc = DateTime.UtcNow;

    var otp = SelfServiceOtp.ForPhone(tenantId, "+48501234567", "hash-xyz", nowUtc, TimeSpan.FromMinutes(10));

    Assert.Equal("+48501234567", otp.PhoneE164);
    Assert.Null(otp.Email);
    Assert.Equal("hash-xyz", otp.CodeHash);
  }

  // DOM-SS-005
  [Fact]
  public void TryConsume_with_correct_hash_marks_consumed_and_issues_session_token()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "good-hash", nowUtc, TimeSpan.FromMinutes(10));

    var ok = otp.TryConsume("good-hash", nowUtc, TimeSpan.FromMinutes(30));

    Assert.True(ok);
    Assert.True(otp.Consumed);
    Assert.NotNull(otp.SessionToken);
    Assert.NotNull(otp.SessionExpiresAtUtc);
    Assert.Equal(nowUtc.AddMinutes(30), otp.SessionExpiresAtUtc!.Value);
  }

  // DOM-SS-006
  [Fact]
  public void TryConsume_returns_false_when_expired()
  {
    var created = DateTime.UtcNow.AddMinutes(-20);
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", created, TimeSpan.FromMinutes(10));

    var ok = otp.TryConsume("h", DateTime.UtcNow, TimeSpan.FromMinutes(30));

    Assert.False(ok);
    Assert.False(otp.Consumed);
  }

  // DOM-SS-007
  [Fact]
  public void TryConsume_increments_FailedAttempts_on_wrong_hash()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "good", nowUtc, TimeSpan.FromMinutes(10));

    Assert.False(otp.TryConsume("bad", nowUtc, TimeSpan.FromMinutes(30)));
    Assert.False(otp.TryConsume("bad-again", nowUtc, TimeSpan.FromMinutes(30)));

    Assert.Equal(2, otp.FailedAttempts);
    Assert.False(otp.Consumed);
  }

  // DOM-SS-008
  [Fact]
  public void IsBlocked_true_after_three_failed_attempts()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "good", nowUtc, TimeSpan.FromMinutes(10));

    for (var i = 0; i < 3; i++)
    {
      otp.TryConsume("bad", nowUtc, TimeSpan.FromMinutes(30));
    }

    Assert.True(otp.IsBlocked);
  }

  // DOM-SS-009
  [Fact]
  public void TryConsume_after_already_consumed_returns_false()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));
    Assert.True(otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30)));

    var second = otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30));

    Assert.False(second);
  }

  // DOM-SS-010
  [Fact]
  public void IsBlocked_true_when_consumed_regardless_of_failed_attempts()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));
    otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30));

    Assert.True(otp.IsBlocked);
  }

  // DOM-SS-011
  [Fact]
  public void IsSessionValid_returns_true_for_matching_token_within_lifetime()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));
    otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30));

    Assert.True(otp.IsSessionValid(otp.SessionToken!.Value, nowUtc.AddMinutes(15)));
  }

  // DOM-SS-012
  [Fact]
  public void IsSessionValid_returns_false_for_wrong_token()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));
    otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30));

    Assert.False(otp.IsSessionValid(Guid.NewGuid(), nowUtc.AddMinutes(5)));
  }

  // DOM-SS-013
  [Fact]
  public void IsSessionValid_returns_false_after_session_expires()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));
    otp.TryConsume("h", nowUtc, TimeSpan.FromMinutes(30));

    Assert.False(otp.IsSessionValid(otp.SessionToken!.Value, nowUtc.AddMinutes(60)));
  }

  // DOM-SS-014
  [Fact]
  public void IsSessionValid_returns_false_when_not_yet_consumed()
  {
    var nowUtc = DateTime.UtcNow;
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.co", "h", nowUtc, TimeSpan.FromMinutes(10));

    Assert.False(otp.IsSessionValid(Guid.NewGuid(), nowUtc.AddMinutes(5)));
  }

  // DOM-SS-015: sesja zweryfikowanego kontaktu rodzi się Consumed, z ważnym tokenem i bez użytego kodu.
  [Fact]
  public void CreateVerifiedSession_is_born_consumed_with_valid_session()
  {
    var tenantId = Guid.NewGuid();
    var nowUtc = new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

    var otp = SelfServiceOtp.CreateVerifiedSession(
      tenantId, "Guest@Example.COM", null, nowUtc,
      codeRetention: TimeSpan.FromMinutes(10), sessionLifetime: TimeSpan.FromHours(2));

    Assert.Equal("guest@example.com", otp.Email);
    Assert.Null(otp.PhoneE164);
    Assert.True(otp.Consumed);
    Assert.Equal(string.Empty, otp.CodeHash);
    Assert.NotNull(otp.SessionToken);
    Assert.Equal(nowUtc.AddHours(2), otp.SessionExpiresAtUtc);
    // ExpiresAtUtc (wiek kodu) = retencja, niezależnie od żywotności sesji — sprzątanie bez zmian.
    Assert.Equal(nowUtc.AddMinutes(10), otp.ExpiresAtUtc);
    Assert.True(otp.IsSessionValid(otp.SessionToken!.Value, nowUtc.AddHours(1)));
    Assert.False(otp.IsSessionValid(otp.SessionToken!.Value, nowUtc.AddHours(3)));
  }

  // DOM-SS-016: wariant telefoniczny — phone ustawiony, email null.
  [Fact]
  public void CreateVerifiedSession_phone_variant_sets_phone_only()
  {
    var otp = SelfServiceOtp.CreateVerifiedSession(
      Guid.NewGuid(), null, "+48501234567", DateTime.UtcNow,
      TimeSpan.FromMinutes(10), TimeSpan.FromHours(2));

    Assert.Null(otp.Email);
    Assert.Equal("+48501234567", otp.PhoneE164);
    Assert.True(otp.Consumed);
    Assert.NotNull(otp.SessionToken);
  }
}
