using System.Net;
using App.Application.Notifications.Push;
using Microsoft.Extensions.Logging;
using WebPush;

namespace App.Infrastructure.Notifications.Push;

/// <summary>
/// Wysyła Web Push protokołem VAPID przez bibliotekę <c>WebPush</c>. Typed HttpClient z jawnym
/// timeoutem (jak SMS/Stripe). 404/410 od push-service = subskrypcja martwa → sygnał do skasowania.
/// </summary>
public sealed class WebPushSender : IWebPushSender
{
  private readonly WebPushClient _client;
  private readonly IWebPushKeys _keys;
  private readonly ILogger<WebPushSender> _logger;

  public WebPushSender(HttpClient httpClient, IWebPushKeys keys, ILogger<WebPushSender> logger)
  {
    _client = new WebPushClient(httpClient);
    _keys = keys;
    _logger = logger;
  }

  public async Task<WebPushSendResult> SendAsync(
    string endpoint,
    string p256dh,
    string auth,
    string payloadJson,
    CancellationToken ct)
  {
    if (!_keys.IsConfigured)
    {
      _logger.LogDebug("WebPush pominięty — brak skonfigurowanych kluczy VAPID.");
      return WebPushSendResult.Failed;
    }

    var subscription = new WebPush.PushSubscription(endpoint, p256dh, auth);
    var vapid = new VapidDetails(_keys.Subject, _keys.PublicKey, _keys.PrivateKey);

    try
    {
      await _client.SendNotificationAsync(subscription, payloadJson, vapid, ct);
      return WebPushSendResult.Delivered;
    }
    catch (WebPushException ex) when (
      ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
    {
      // Subskrypcja cofnięta/wygasła — kanał ją skasuje.
      return WebPushSendResult.Expired;
    }
    catch (WebPushException ex)
    {
      _logger.LogWarning(
        "WebPush push-service zwrócił {Status} dla subskrypcji.", (int)ex.StatusCode);
      return WebPushSendResult.Failed;
    }
  }
}
