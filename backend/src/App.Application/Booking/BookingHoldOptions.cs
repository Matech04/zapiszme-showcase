using System.ComponentModel.DataAnnotations;

namespace App.Application.Booking;

/// <summary>
/// TTL-e holdu rezerwacji publicznej. Konfigurowane przez sekcję <c>Booking</c> w appsettings.
/// Krótkie TTL w env E2E pozwalają testom uniknąć minutowego czekania na expiry.
/// </summary>
public sealed class BookingHoldOptions
{
  public const string Section = "Booking";

  /// <summary>
  /// TTL holdu od wyboru slotu do POST /otp/request. Krótkie okno bo squatter który
  /// nigdy nie tknie OTP zwalnia slot po tym czasie. Default: 60s.
  /// </summary>
  [Range(1, 3600)]
  public int HoldTtlSeconds { get; set; } = 60;

  /// <summary>
  /// Wydłużenie lease po faktycznym requestcie OTP — czas na odebranie kodu i wpisanie.
  /// Default: 180s (3 min).
  /// </summary>
  [Range(1, 3600)]
  public int OtpLeaseSeconds { get; set; } = 180;

  public TimeSpan HoldTtl => TimeSpan.FromSeconds(HoldTtlSeconds);
  public TimeSpan OtpLease => TimeSpan.FromSeconds(OtpLeaseSeconds);
}
