using System.Text.Json.Serialization;
using App.Api.Options;
using Microsoft.Extensions.Options;

namespace App.Api.Security;

public sealed class CloudflareTurnstileVerifier : ITurnstileVerifier
{
  private readonly HttpClient _httpClient;
  private readonly IOptions<TurnstileOptions> _options;
  private readonly ILogger<CloudflareTurnstileVerifier> _logger;
  private readonly IWebHostEnvironment _environment;

  public CloudflareTurnstileVerifier(
    HttpClient httpClient,
    IOptions<TurnstileOptions> options,
    ILogger<CloudflareTurnstileVerifier> logger,
    IWebHostEnvironment environment)
  {
    _httpClient = httpClient;
    _options = options;
    _logger = logger;
    _environment = environment;
  }

  public async Task<bool> VerifyAsync(
    string? token,
    string? remoteIp,
    TurnstileWidgetKind kind = TurnstileWidgetKind.Managed,
    CancellationToken cancellationToken = default)
  {
    var options = _options.Value;
    if (!options.Enabled)
    {
      return true;
    }

    // W Development/Testing brak tokenu nie blokuje — frontend dev często nie ma
    // skonfigurowanego site-key (PUBLIC_TURNSTILE_*), a Turnstile:Enabled bywa wymuszony
    // przez user-secrets/env. Token JEŚLI dotarł, jest niżej weryfikowany normalnie.
    // Produkcja (w tym LOCAL_PROD = env Production) zawsze wymaga ważnego tokenu.
    if (string.IsNullOrWhiteSpace(token)
        && (_environment.IsDevelopment() || _environment.IsEnvironment("Testing")))
    {
      return true;
    }

    // Wybór secret-key po typie widgetu — CF paruje token tylko z secretem swojego widgetu.
    var secret = kind switch
    {
      TurnstileWidgetKind.Invisible => options.InvisibleSecretKey,
      _ => options.SecretKey,
    };

    if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
    {
      return false;
    }

    var form = new Dictionary<string, string>
    {
      ["secret"] = secret,
      ["response"] = token,
    };

    if (!string.IsNullOrWhiteSpace(remoteIp))
    {
      form["remoteip"] = remoteIp;
    }

    using var response = await _httpClient.PostAsync(
      options.VerifyUrl,
      new FormUrlEncodedContent(form),
      cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      _logger.LogWarning(
        "Cloudflare Turnstile verification returned HTTP {StatusCode}",
        (int)response.StatusCode);
      return false;
    }

    var payload = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(
      cancellationToken: cancellationToken);

    if (payload?.Success == true)
    {
      return true;
    }

    _logger.LogWarning(
      "Cloudflare Turnstile verification failed with errors: {Errors}",
      payload?.ErrorCodes is { Length: > 0 } ? string.Join(",", payload.ErrorCodes) : "unknown");
    return false;
  }

  private sealed record TurnstileVerifyResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
