namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Wysyła pojedynczy SMS przez smsapi.pl. Implementacja produkcyjna używa <c>HttpClient</c>;
/// testy podmieniają na fakea, żeby nie wołać prawdziwego API.
/// </summary>
public interface ISmsApiClient
{
  /// <summary>
  /// Wysyła wiadomość. Numer musi być znormalizowany do formatu <c>48xxxxxxxxx</c> (bez <c>+</c>).
  /// Rzuca <see cref="SmsApiException"/> przy błędzie z API, <see cref="HttpRequestException"/>
  /// przy problemach sieciowych.
  /// </summary>
  Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct);
}
