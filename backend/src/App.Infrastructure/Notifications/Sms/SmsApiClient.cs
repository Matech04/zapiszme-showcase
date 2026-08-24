using System.Net.Http.Headers;
using System.Text.Json;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Klient REST smsapi.pl. POST <c>sms.do</c>, autoryzacja OAuth Bearer. Form-urlencoded.
/// Per-iteracja: brak retry/circuit-breakera (dispatcher i tak izoluje awarie kanału);
/// timeout konfigurowany przez <see cref="SmsApiOptions.TimeoutSeconds"/>.
/// </summary>
public sealed class SmsApiClient : ISmsApiClient
{
  private readonly HttpClient _http;
  private readonly IOptions<SmsApiOptions> _options;
  private readonly IMemoryCache _cache;
  private readonly TimeProvider _timeProvider;
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly ILogger<SmsApiClient> _logger;

  public SmsApiClient(
    HttpClient http,
    IOptions<SmsApiOptions> options,
    IMemoryCache cache,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SmsApiClient> logger)
  {
    _http = http;
    _options = options;
    _cache = cache;
    _timeProvider = timeProvider;
    _httpContextAccessor = httpContextAccessor;
    _logger = logger;
  }

  public async Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct)
  {
    var opts = _options.Value;
    if (string.IsNullOrWhiteSpace(opts.OAuthToken))
    {
      throw new InvalidOperationException(
        "smsapi.pl OAuth token nie jest skonfigurowany (Sms:OAuthToken). "
        + "Nie wysłano SMS-a — sprawdź sekret SMSAPI__OAUTH_TOKEN.");
    }

    // Globalny kill-switch budżetu (cross-tenant). Sprawdzamy PRZED wysyłką. TestMode nie nalicza
    // kredytów, więc go pomijamy. Liczba realnie wysłanych SMS/dobę trzyma się w pamięci procesu.
    // ZAŁOŻENIE single-instance (MVP): cap globalny/per-IP żyje w IMemoryCache, więc resetuje się
    // przy restarcie/deployu i NIE jest współdzielony między instancjami. Przy skalowaniu do >1
    // instancji API te liczniki MUSZĄ przejść na wspólny store (Redis INCR+TTL / DB) — inaczej
    // realny sufit = cap × liczba instancji. Patrz SmsApiOptions.
    var dailyKey = GlobalDailyKey();
    if (!opts.TestMode && opts.GlobalDailyCap > 0)
    {
      var sentToday = _cache.TryGetValue(dailyKey, out int n) ? n : 0;
      if (sentToday >= opts.GlobalDailyCap)
      {
        _logger.LogError(
          "GlobalSmsDailyCap reached ({Cap}/day) — SMS send blocked (kill-switch). Category={Category}",
          opts.GlobalDailyCap, "audit");
        throw new SmsServiceUnavailableException(
          "Dzienny globalny limit SMS został osiągnięty — wysyłka tymczasowo wstrzymana.");
      }
    }

    // Per-IP dzienny backstop — ogranicza pojedynczy (nierotujący) IP. Tylko dla SMS inicjowanych
    // żądaniem HTTP; tło (przypomnienia) nie ma HttpContext → pomijane. Po ForwardedHeaders
    // RemoteIpAddress to realny IP klienta (za Caddy/Cloudflare).
    var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    var ipDailyKey = clientIp is null ? null : IpDailyKey(clientIp);
    if (!opts.TestMode && opts.MaxSmsPerIpPerDay > 0 && ipDailyKey is not null)
    {
      var sentFromIp = _cache.TryGetValue(ipDailyKey, out int ipN) ? ipN : 0;
      if (sentFromIp >= opts.MaxSmsPerIpPerDay)
      {
        _logger.LogWarning(
          "PerIpSmsDailyCap reached ({Cap}/day) for a single IP — SMS send blocked. Category={Category}",
          opts.MaxSmsPerIpPerDay, "audit");
        throw new SmsServiceUnavailableException(
          "Przekroczono dzienny limit SMS dla tego adresu — spróbuj ponownie jutro.");
      }
    }

    var form = new List<KeyValuePair<string, string>>
    {
      new("to", to),
      new("message", text),
      new("from", opts.SenderName),
      new("format", "json"),
      new("encoding", "utf-8"),
    };
    if (opts.TestMode)
    {
      form.Add(new("test", "1"));
    }

    using var request = new HttpRequestMessage(HttpMethod.Post, "sms.do")
    {
      Content = new FormUrlEncodedContent(form),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.OAuthToken);

    using var response = await _http.SendAsync(request, ct);
    var body = await response.Content.ReadAsStringAsync(ct);

    if (!response.IsSuccessStatusCode)
    {
      // NIE logujemy surowego body — odpowiedź smsapi może zawierać numer odbiorcy (PII), a klucz
      // property „Body" nie jest maskowany przez SensitiveDataMaskingEnricher. Logujemy tylko status
      // i numeryczny kod błędu smsapi (bez treści/numeru).
      _logger.LogWarning(
        "smsapi.pl HTTP {Status}, kod błędu {ErrorCode}.",
        (int)response.StatusCode, TryReadSmsErrorCode(body));
      throw new HttpRequestException(
        $"smsapi.pl zwrócił HTTP {(int)response.StatusCode}.");
    }

    var result = ParseResponse(body);

    // Naliczamy do globalnego dziennego licznika dopiero po udanej wysyłce (TestMode pomijamy —
    // nie kosztuje kredytów). Klucz wygasa po dobie; reset procesu zeruje (akceptowalne dla MVP).
    if (!opts.TestMode && opts.GlobalDailyCap > 0)
    {
      var sentToday = _cache.TryGetValue(dailyKey, out int n) ? n : 0;
      _cache.Set(dailyKey, sentToday + 1, new MemoryCacheEntryOptions
      {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
      });
    }

    // Per-IP dzienny licznik — inkrementujemy po udanej wysyłce (jak globalny).
    if (!opts.TestMode && opts.MaxSmsPerIpPerDay > 0 && ipDailyKey is not null)
    {
      var sentFromIp = _cache.TryGetValue(ipDailyKey, out int ipN) ? ipN : 0;
      _cache.Set(ipDailyKey, sentFromIp + 1, new MemoryCacheEntryOptions
      {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
      });
    }

    return result;
  }

  private string GlobalDailyKey() =>
    $"sms:global-daily:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd}";

  private string IpDailyKey(string ip) =>
    $"sms:ip-daily:{ip}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd}";

  private static SmsSendResult ParseResponse(string body)
  {
    using var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;

    if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Number)
    {
      var code = errorEl.GetInt32();
      var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "" : "";
      throw new SmsApiException(code, $"smsapi.pl error {code}: {msg}");
    }

    if (!root.TryGetProperty("list", out var listEl) || listEl.GetArrayLength() == 0)
    {
      // Bez numeru odbiorcy w komunikacie — wyjątek bywa logowany, a numer to PII.
      throw new SmsApiException(-1, "Nieoczekiwana odpowiedź smsapi.pl: brak pola 'list'.");
    }

    var first = listEl[0];
    var id = first.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
    var status = first.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "" : "";
    var points = first.TryGetProperty("points", out var ptEl) && ptEl.ValueKind == JsonValueKind.Number
      ? ptEl.GetDecimal()
      : 0m;
    return new SmsSendResult(id, status, points);
  }

  /// <summary>Wyciąga TYLKO numeryczny kod błędu smsapi z body — bez treści/numeru odbiorcy (PII).</summary>
  private static string TryReadSmsErrorCode(string body)
  {
    try
    {
      using var doc = JsonDocument.Parse(body);
      if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Number)
      {
        return e.GetInt32().ToString();
      }
    }
    catch (JsonException)
    {
      // body nie-JSON / puste → brak kodu
    }
    return "n/d";
  }
}
