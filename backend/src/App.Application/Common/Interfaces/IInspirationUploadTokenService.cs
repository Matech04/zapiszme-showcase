namespace App.Application.Common.Interfaces;

/// <summary>
/// Krótkożyjący, podpisany (DataProtection) token autoryzujący upload zdjęć inspiracji do KONKRETNEJ
/// wizyty — wydawany dopiero po potwierdzeniu OTP. Dzięki temu zdjęcia trafiają na storage WYŁĄCZNIE
/// dla potwierdzonych rezerwacji (zero sierot, brak otwartego endpointu do spamowania uploadem), a
/// token nie autoryzuje niczego innego niż upload inspiracji do tej jednej wizyty.
/// </summary>
public interface IInspirationUploadTokenService
{
  /// <summary>Wydaje token uploadu dla danej wizyty (TTL wewnętrzny — kilkanaście minut).</summary>
  string Issue(Guid appointmentId);

  /// <summary>Waliduje token; zwraca <c>true</c> + <paramref name="appointmentId"/> gdy podpis i ważność OK.</summary>
  bool TryValidate(string token, out Guid appointmentId);
}
