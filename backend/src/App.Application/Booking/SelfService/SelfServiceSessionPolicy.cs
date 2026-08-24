namespace App.Application.Booking.SelfService;

/// <summary>
/// Wspólna polityka czasów sesji zweryfikowanego kontaktu (panel zarządzania + pomijanie OTP przy
/// ponownej rezerwacji). Trzymane w jednym miejscu, bo używają tego zarówno
/// <c>VerifySelfServiceOtpCommand</c> (panel), jak i mennica sesji w <c>VerifyOtpCommand</c> (rezerwacja).
/// </summary>
internal static class SelfServiceSessionPolicy
{
  /// <summary>
  /// Żywotność tokenu sesji (absolutna, bez przedłużania aktywnością). 2h: pokrywa spokojną wizytę
  /// in-session, a jednocześnie mieści się znacznie poniżej 48h retencji sprzątania OTP
  /// (<c>OtpCleanupHostedService</c>), więc nie wymaga zmian w sprzątaniu.
  /// </summary>
  public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

  /// <summary>
  /// „Wiek kodu” ustawiany dla wiersza sesji tworzonego bez wyzwania kodem
  /// (<see cref="App.Domain.Aggregates.SelfServiceAggregate.SelfServiceOtp.CreateVerifiedSession"/>).
  /// Spójny z TTL zwykłego OTP (10 min) — sprzątanie po wieku kodu działa bez zmian.
  /// </summary>
  public static readonly TimeSpan VerifiedSessionCodeRetention = TimeSpan.FromMinutes(10);
}
