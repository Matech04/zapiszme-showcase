using App.Application.Common.Security;
using App.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Implementacja <see cref="IPhoneOtpSender"/> używająca <see cref="ISmsApiClient"/> (smsapi.pl).
/// Zdejmuje <c>+</c> z E.164 (smsapi.pl wymaga <c>48xxxxxxxxx</c>) i wrapuje błędy w
/// <see cref="SmsServiceUnavailableException"/>.
/// </summary>
public sealed class SmsApiPhoneOtpSender : IPhoneOtpSender
{
  private readonly ISmsApiClient _smsApi;
  private readonly ILogger<SmsApiPhoneOtpSender> _logger;

  public SmsApiPhoneOtpSender(ISmsApiClient smsApi, ILogger<SmsApiPhoneOtpSender> logger)
  {
    _smsApi = smsApi;
    _logger = logger;
  }

  public async Task SendOtpAsync(string phoneE164, string code, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(phoneE164))
    {
      throw new ArgumentException("Numer telefonu jest pusty.", nameof(phoneE164));
    }
    if (string.IsNullOrWhiteSpace(code))
    {
      throw new ArgumentException("Kod OTP jest pusty.", nameof(code));
    }

    var to = phoneE164.StartsWith('+') ? phoneE164[1..] : phoneE164;
    // Bez "zapisz.me" — smsapi.pl traktuje to jako URL i odrzuca z error 94 (anti-spam).
    // Sender ("2WAY"/"INFO") i tak identyfikuje nadawcę w SMS-ie.
    var text = $"Twoj kod: {code}. Wazny 10 min.";

    try
    {
      var result = await _smsApi.SendAsync(to, text, ct);
      _logger.LogInformation(
        "Phone OTP SMS sent: status={Status}, id={Id}, points={Points}",
        result.Status, result.Id, result.Points);
    }
    catch (SmsApiException ex)
    {
      _logger.LogError(ex, "smsapi.pl odrzucił żądanie OTP (code={Code}).", ex.ErrorCode);
      throw new SmsServiceUnavailableException(ex.Message, ex);
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "Sieć/HTTP nie wytrzymały wysyłki OTP.");
      throw new SmsServiceUnavailableException("Problem sieciowy podczas wysyłki SMS.", ex);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "SmsApiClient nie skonfigurowany (brak tokenu OAuth).");
      throw new SmsServiceUnavailableException(
        "Usługa SMS jest tymczasowo niedostępna — skontaktuj się z administratorem.", ex);
    }
  }
}
