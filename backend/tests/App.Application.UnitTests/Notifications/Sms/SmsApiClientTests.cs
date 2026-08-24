using System.Net;
using System.Text;
using App.Domain.Exceptions;
using App.Infrastructure.Notifications.Sms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>SMS-API-* — klient HTTP smsapi.pl: happy path, błędy JSON, błędy HTTP, test mode, brak tokenu.</summary>
public sealed class SmsApiClientTests
{
  [Fact]
  public async Task SendAsync_HappyPath_ParsesIdAndStatus()
  {
    var json =
      """{"count":1,"list":[{"id":"1234567890","points":0.16,"number":"48501234567","status":"QUEUE"}]}""";
    var (client, handler) = CreateClient(json, HttpStatusCode.OK, oauthToken: "tok");

    var result = await client.SendAsync("48501234567", "Test SMS", TestContext.Current.CancellationToken);

    Assert.Equal("1234567890", result.Id);
    Assert.Equal("QUEUE", result.Status);
    Assert.Equal(0.16m, result.Points);
    Assert.Single(handler.Requests);
    Assert.Equal("Bearer tok", handler.Requests[0].Headers.Authorization?.ToString());
  }

  [Fact]
  public async Task SendAsync_ErrorJson_ThrowsSmsApiException()
  {
    var json = """{"error":13,"message":"No correct phone numbers"}""";
    var (client, _) = CreateClient(json, HttpStatusCode.OK, oauthToken: "tok");

    var ex = await Assert.ThrowsAsync<SmsApiException>(
      () => client.SendAsync("48501234567", "x", TestContext.Current.CancellationToken));
    Assert.Equal(13, ex.ErrorCode);
  }

  [Fact]
  public async Task SendAsync_Non2xx_ThrowsHttpRequestException()
  {
    var (client, _) = CreateClient("oops", HttpStatusCode.InternalServerError, oauthToken: "tok");

    await Assert.ThrowsAsync<HttpRequestException>(
      () => client.SendAsync("48501234567", "x", TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task SendAsync_TestModeOn_SendsTestParam()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0,"number":"48501234567","status":"QUEUE"}]}""";
    var (client, handler) = CreateClient(json, HttpStatusCode.OK, oauthToken: "tok", testMode: true);

    await client.SendAsync("48501234567", "x", TestContext.Current.CancellationToken);

    Assert.Contains("test=1", handler.Bodies[0]);
  }

  [Fact]
  public async Task SendAsync_TestModeOff_DoesNotSendTestParam()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0,"number":"48501234567","status":"QUEUE"}]}""";
    var (client, handler) = CreateClient(json, HttpStatusCode.OK, oauthToken: "tok", testMode: false);

    await client.SendAsync("48501234567", "x", TestContext.Current.CancellationToken);

    Assert.DoesNotContain("test=1", handler.Bodies[0]);
  }

  [Fact]
  public async Task SendAsync_EmptyToken_Throws()
  {
    var (client, _) = CreateClient("{}", HttpStatusCode.OK, oauthToken: "");

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.SendAsync("48501234567", "x", TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task SendAsync_RequestFormat_IncludesToMessageFromAndFormat()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0,"number":"48501234567","status":"QUEUE"}]}""";
    var (client, handler) = CreateClient(
      json, HttpStatusCode.OK, oauthToken: "tok", senderName: "ZAPISZME");

    await client.SendAsync("48501234567", "Witaj", TestContext.Current.CancellationToken);

    Assert.Contains("to=48501234567", handler.Bodies[0]);
    Assert.Contains("message=Witaj", handler.Bodies[0]);
    Assert.Contains("from=ZAPISZME", handler.Bodies[0]);
    Assert.Contains("format=json", handler.Bodies[0]);
  }

  // ── [M2] Global kill-switch + per-IP cap (jedyny twardy sufit kosztu SMS) ─────────────────────

  [Fact]
  public async Task SendAsync_GlobalCapReached_ThrowsAndDoesNotCallApi()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0.1,"number":"48501234567","status":"QUEUE"}]}""";
    var cache = new MemoryCache(new MemoryCacheOptions());
    var (client, handler) = CreateClient(
      json, HttpStatusCode.OK, oauthToken: "tok", globalDailyCap: 2, cache: cache);
    var ct = TestContext.Current.CancellationToken;

    // 2 udane wysyłki naliczają licznik do capu (2).
    await client.SendAsync("48501234567", "x", ct);
    await client.SendAsync("48501234567", "x", ct);
    Assert.Equal(2, handler.Requests.Count);

    // 3. wysyłka — kill-switch rzuca PRZED HTTP (liczba żądań nie rośnie).
    await Assert.ThrowsAsync<SmsServiceUnavailableException>(
      () => client.SendAsync("48501234567", "x", ct));
    Assert.Equal(2, handler.Requests.Count);
  }

  [Fact]
  public async Task SendAsync_PerIpCapReached_Throws()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0.1,"number":"48501234567","status":"QUEUE"}]}""";
    var cache = new MemoryCache(new MemoryCacheOptions());
    var (client, handler) = CreateClient(
      json, HttpStatusCode.OK, oauthToken: "tok", maxSmsPerIpPerDay: 1, clientIp: "203.0.113.7", cache: cache);
    var ct = TestContext.Current.CancellationToken;

    await client.SendAsync("48501234567", "x", ct);

    var ex = await Assert.ThrowsAsync<SmsServiceUnavailableException>(
      () => client.SendAsync("48501234567", "x", ct));
    Assert.NotNull(ex);
    // Drugie żądanie zablokowane przed HTTP.
    Assert.Equal(1, handler.Requests.Count);
  }

  [Fact]
  public async Task SendAsync_TestMode_DoesNotCountTowardCap()
  {
    var json = """{"count":1,"list":[{"id":"x","points":0,"number":"48501234567","status":"QUEUE"}]}""";
    var cache = new MemoryCache(new MemoryCacheOptions());
    var (client, handler) = CreateClient(
      json, HttpStatusCode.OK, oauthToken: "tok", testMode: true, globalDailyCap: 1, cache: cache);
    var ct = TestContext.Current.CancellationToken;

    // W TestMode licznik się NIE nalicza — choć cap=1, wiele wysyłek przechodzi.
    await client.SendAsync("48501234567", "x", ct);
    await client.SendAsync("48501234567", "x", ct);
    await client.SendAsync("48501234567", "x", ct);

    Assert.Equal(3, handler.Requests.Count);
  }

  [Fact]
  public async Task SendAsync_Increments_OnlyAfterSuccess()
  {
    // 1. wysyłka: smsapi zwraca błąd w JSON (rzuca PO HTTP, PRZED naliczeniem) → licznik = 0.
    var errorJson = """{"error":13,"message":"No correct phone numbers"}""";
    var cache = new MemoryCache(new MemoryCacheOptions());
    var (failingClient, _) = CreateClient(
      errorJson, HttpStatusCode.OK, oauthToken: "tok", globalDailyCap: 1, cache: cache);
    var ct = TestContext.Current.CancellationToken;

    await Assert.ThrowsAsync<SmsApiException>(() => failingClient.SendAsync("48501234567", "x", ct));

    // Mimo cap=1, nieudana wysyłka NIE naliczyła licznika — kolejna (udana) na tym samym cache przechodzi.
    var okJson = """{"count":1,"list":[{"id":"x","points":0.1,"number":"48501234567","status":"QUEUE"}]}""";
    var (okClient, okHandler) = CreateClient(
      okJson, HttpStatusCode.OK, oauthToken: "tok", globalDailyCap: 1, cache: cache);
    await okClient.SendAsync("48501234567", "x", ct);
    Assert.Single(okHandler.Requests);

    // Teraz licznik = 1 (po udanej) → następna blokowana kill-switchem.
    var (okClient2, _) = CreateClient(
      okJson, HttpStatusCode.OK, oauthToken: "tok", globalDailyCap: 1, cache: cache);
    await Assert.ThrowsAsync<SmsServiceUnavailableException>(
      () => okClient2.SendAsync("48501234567", "x", ct));
  }

  private static (SmsApiClient client, RecordingHandler handler) CreateClient(
    string responseBody,
    HttpStatusCode statusCode,
    string oauthToken,
    bool testMode = false,
    string senderName = "INFO",
    int globalDailyCap = 0,
    int maxSmsPerIpPerDay = 0,
    string? clientIp = null,
    MemoryCache? cache = null)
  {
    var handler = new RecordingHandler(responseBody, statusCode);
    var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.smsapi.pl/") };
    var options = Options.Create(new SmsApiOptions
    {
      OAuthToken = oauthToken,
      TestMode = testMode,
      SenderName = senderName,
      GlobalDailyCap = globalDailyCap,
      MaxSmsPerIpPerDay = maxSmsPerIpPerDay,
    });
    cache ??= new MemoryCache(new MemoryCacheOptions());
    var httpContextAccessor = new HttpContextAccessor();
    if (clientIp is not null)
    {
      httpContextAccessor.HttpContext = new DefaultHttpContext
      {
        Connection = { RemoteIpAddress = System.Net.IPAddress.Parse(clientIp) },
      };
    }
    return (new SmsApiClient(http, options, cache, TimeProvider.System, httpContextAccessor, NullLogger<SmsApiClient>.Instance), handler);
  }

  private sealed class RecordingHandler : HttpMessageHandler
  {
    private readonly string _body;
    private readonly HttpStatusCode _status;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public RecordingHandler(string body, HttpStatusCode status)
    {
      _body = body;
      _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Requests.Add(request);
      Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
      return new HttpResponseMessage(_status)
      {
        Content = new StringContent(_body, Encoding.UTF8, "application/json"),
      };
    }
  }
}
