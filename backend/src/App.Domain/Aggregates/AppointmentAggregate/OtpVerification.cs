namespace App.Domain.Aggregates.AppointmentAggregate;

/// <summary>OTP powiązany z wizytą (telefon lub e-mail). Persystowane jako owned przez EF Core.</summary>
public sealed class OtpVerification
{
  public OtpVerificationChannel Channel { get; private set; }

  /// <summary>E.164, gdy <see cref="Channel"/> = Phone.</summary>
  public string? PhoneE164 { get; private set; }

  /// <summary>Znormalizowany na lower; gdy <see cref="Channel"/> = Email.</summary>
  public string? Email { get; private set; }

  public string OtpHash { get; private set; } = null!;

  public DateTime ExpiryTimeUtc { get; private set; }

  private OtpVerification()
  {
  }

  public static OtpVerification ForPhone(PhoneNumber phoneNumber, string rawOtp, DateTime expiryTimeUtc)
  {
    return new OtpVerification
    {
      Channel = OtpVerificationChannel.Phone,
      PhoneE164 = phoneNumber.Value,
      Email = null,
      OtpHash = BCrypt.Net.BCrypt.HashPassword(rawOtp),
      ExpiryTimeUtc = expiryTimeUtc
    };
  }

  public static OtpVerification ForEmail(string email, string rawOtp, DateTime expiryTimeUtc)
  {
    var norm = GuardEmail(email);
    return new OtpVerification
    {
      Channel = OtpVerificationChannel.Email,
      PhoneE164 = null,
      Email = norm,
      OtpHash = BCrypt.Net.BCrypt.HashPassword(rawOtp),
      ExpiryTimeUtc = expiryTimeUtc
    };
  }

  private static string GuardEmail(string email)
  {
    if (string.IsNullOrWhiteSpace(email))
    {
      throw new ArgumentException("E-mail jest wymagany.", nameof(email));
    }

    var t = email.Trim();
    if (t.Length > 254 || !t.Contains('@', StringComparison.Ordinal))
    {
      throw new ArgumentException("Nieprawidłowy adres e-mail.", nameof(email));
    }

    return t.ToLowerInvariant();
  }

  public bool IsValid(string userInputCode, DateTime currentTime)
  {
    if (currentTime > ExpiryTimeUtc) {
      return false;
    }

    return BCrypt.Net.BCrypt.Verify(userInputCode, OtpHash);
  }
}
