namespace App.Application.Common.Security;

/// <summary>
/// Abstrakcja wysyłki kodu SMS (rejestracja, recover). Konkretna implementacja w warstwie
/// Infrastructure deleguje do <c>ISmsApiClient</c> (smsapi.pl). Tu trzymamy interfejs żeby
/// handlery z App.Application nie wymagały referencji do App.Infrastructure.
/// </summary>
public interface IPhoneOtpSender
{
  /// <summary>
  /// Wysyła SMS z 6-cyfrowym kodem. <paramref name="phoneE164"/> w formacie <c>+48xxxxxxxxx</c>.
  /// Rzuca <see cref="App.Domain.Exceptions.SmsServiceUnavailableException"/> przy awarii.
  /// </summary>
  Task SendOtpAsync(string phoneE164, string code, CancellationToken ct);
}
