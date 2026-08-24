using App.Application.Common.Security;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Dev backdoor: zamiast wywoływać smsapi.pl, loguje kod plain-text na poziomie Information.
/// Pozwala testować flow rejestracji bez tokenu OAuth i bez kosztów. Aktywowany przez
/// <c>Sms:DevBackdoorLogCode=true</c> w <c>appsettings.Development.json</c>. Program.cs
/// blokuje aktywację w env <c>Production</c> — jako defense-in-depth na konfigurację.
/// </summary>
public sealed class LoggingPhoneOtpSender : IPhoneOtpSender
{
  private readonly ILogger<LoggingPhoneOtpSender> _logger;

  public LoggingPhoneOtpSender(ILogger<LoggingPhoneOtpSender> logger) => _logger = logger;

  public Task SendOtpAsync(string phoneE164, string code, CancellationToken ct)
  {
    _logger.LogInformation(
      "[DEV BACKDOOR] Phone OTP — telefon={Phone} kod={Code}. (smsapi.pl pominięte; ustaw Sms:DevBackdoorLogCode=false aby wysłać realny SMS.)",
      phoneE164, code);
    return Task.CompletedTask;
  }
}
