using App.Application.Notifications.Push;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Notifications.Push;

/// <summary>Adapter <see cref="IWebPushKeys"/> nad <see cref="WebPushOptions"/> z konfiguracji.</summary>
public sealed class WebPushKeys : IWebPushKeys
{
  private readonly WebPushOptions _options;

  public WebPushKeys(IOptions<WebPushOptions> options)
  {
    _options = options.Value;
  }

  public string PublicKey => _options.VapidPublicKey;
  public string PrivateKey => _options.VapidPrivateKey;
  public string Subject => _options.Subject;

  public bool IsConfigured =>
    !string.IsNullOrWhiteSpace(_options.VapidPublicKey)
    && !string.IsNullOrWhiteSpace(_options.VapidPrivateKey)
    && !string.IsNullOrWhiteSpace(_options.Subject);
}
