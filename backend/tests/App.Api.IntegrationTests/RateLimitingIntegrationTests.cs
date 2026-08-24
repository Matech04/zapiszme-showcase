using System.Net;
using System.Net.Http.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Domain.Exceptions;

namespace App.Api.IntegrationTests;

public sealed class RateLimitingIntegrationTests
{
  [Fact]
  public async Task Global_rate_limiter_rejects_requests_after_configured_limit()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    for (var i = 0; i < 3; i++)
    {
      var response = await client.GetAsync("/api/booking/missing/public-salon", ct);
      Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    var rejected = await client.GetAsync("/api/booking/missing/public-salon", ct);

    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    AssertHasRetryAfterHeader(rejected);
    await AssertRateLimitProblem(rejected, ct);
  }

  [Fact]
  public async Task Public_booking_write_limiter_rejects_requests_after_configured_limit()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;
    var appointmentId = Guid.NewGuid();

    for (var i = 0; i < 2; i++)
    {
      var response = await client.PostAsJsonAsync(
        $"/api/booking/missing/public-appointment/{appointmentId}/request-otp",
        new { token = Guid.NewGuid(), phoneNumber = (string?)null, email = "booker@example.com" },
        ct);
      Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    var rejected = await client.PostAsJsonAsync(
      $"/api/booking/missing/public-appointment/{appointmentId}/request-otp",
      new { token = Guid.NewGuid(), phoneNumber = (string?)null, email = "booker@example.com" },
      ct);

    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    AssertHasRetryAfterHeader(rejected);
    await AssertRateLimitProblem(rejected, ct);
  }

  [Fact]
  public async Task Public_booking_write_limiter_burst_parallel_requests_returns_some_429()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;
    var appointmentId = Guid.NewGuid();

    const int burstSize = 12;
    var tasks = Enumerable.Range(0, burstSize)
      .Select(_ => client.PostAsJsonAsync(
        $"/api/booking/missing/public-appointment/{appointmentId}/request-otp",
        new { token = Guid.NewGuid(), phoneNumber = (string?)null, email = "booker@example.com" },
        ct));

    var responses = await Task.WhenAll(tasks);
    var tooManyCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
    var nonTooManyCount = responses.Length - tooManyCount;

    Assert.True(tooManyCount > 0, "Expected at least one 429 response under burst load.");
    Assert.True(nonTooManyCount > 0, "Expected at least one non-429 response before limiter engages.");

    foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests))
    {
      AssertHasRetryAfterHeader(rejected);
      await AssertRateLimitProblem(rejected, ct);
    }
  }

  [Fact]
  public async Task Public_booking_write_limiter_cannot_be_bypassed_by_switching_endpoints()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;
    var appointmentId = Guid.NewGuid();

    var first = await client.PostAsJsonAsync(
      $"/api/booking/missing/public-appointment/{appointmentId}/request-otp",
      new { token = Guid.NewGuid(), phoneNumber = (string?)null, email = "booker@example.com" },
      ct);
    Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);

    var second = await client.PostAsJsonAsync(
      $"/api/booking/missing/public-appointment/{appointmentId}/verify-otp",
      new { token = Guid.NewGuid(), otp = "123456" },
      ct);
    Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);

    var third = await client.PostAsJsonAsync(
      "/api/booking/missing/public-appointment/hold",
      new
      {
        serviceIds = new[] { Guid.NewGuid() },
        employeeId = Guid.NewGuid(),
        date = TestDates.IsoInDays(14),
        startTime = "10:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    AssertHasRetryAfterHeader(third);
    await AssertRateLimitProblem(third, ct);
  }

  [Fact]
  public async Task Global_rate_limiter_is_partitioned_per_authenticated_user()
  {
    // Zalogowani jadą po partycji "Authenticated" (osobny, wyższy limit niż anon "Global").
    // W Testing nie definiujemy go w appsettings (default 2000 — pozostałe testy zalogowane
    // robią >3 żądań), więc na potrzeby tego testu zbijamy limit do 3 lokalnie.
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
      builder.UseSetting("RateLimiting:Authenticated:PermitLimit", "3"));
    var userAClient = factory.CreateClient();
    var userBClient = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    userAClient.DefaultRequestHeaders.Add(IntegrationTestAuthHeaders.UserId, Guid.NewGuid().ToString());
    userAClient.DefaultRequestHeaders.Add(IntegrationTestAuthHeaders.Roles, "Owner");

    userBClient.DefaultRequestHeaders.Add(IntegrationTestAuthHeaders.UserId, Guid.NewGuid().ToString());
    userBClient.DefaultRequestHeaders.Add(IntegrationTestAuthHeaders.Roles, "Owner");

    for (var i = 0; i < 3; i++)
    {
      var response = await userAClient.GetAsync("/api/booking/missing/public-salon", ct);
      Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    var userARejected = await userAClient.GetAsync("/api/booking/missing/public-salon", ct);
    Assert.Equal(HttpStatusCode.TooManyRequests, userARejected.StatusCode);
    AssertHasRetryAfterHeader(userARejected);
    await AssertRateLimitProblem(userARejected, ct);

    var userBResponse = await userBClient.GetAsync("/api/booking/missing/public-salon", ct);
    Assert.Equal(HttpStatusCode.NotFound, userBResponse.StatusCode);
  }

  [Fact]
  public async Task Health_live_endpoint_is_not_rate_limited_after_global_limit_exceeded()
  {
    using var factory = new BookingApiApplicationFactory();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    for (var i = 0; i < 3; i++)
    {
      var response = await client.GetAsync("/api/booking/missing/public-salon", ct);
      Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    var rejected = await client.GetAsync("/api/booking/missing/public-salon", ct);
    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

    var live = await client.GetAsync("/health/live", ct);
    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
  }

  [Fact]
  public async Task Global_rate_limiter_allows_requests_again_after_window_resets()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:Global:PermitLimit", "2");
      builder.UseSetting("RateLimiting:Global:WindowSeconds", "2");
      builder.UseSetting("RateLimiting:Global:QueueLimit", "0");
    });

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    for (var i = 0; i < 2; i++)
    {
      var response = await client.GetAsync("/api/booking/missing/public-salon", ct);
      Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    var rejected = await client.GetAsync("/api/booking/missing/public-salon", ct);
    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    AssertHasRetryAfterHeader(rejected);
    await AssertRateLimitProblem(rejected, ct);

    await Task.Delay(TimeSpan.FromMilliseconds(2300), ct);

    var afterReset = await client.GetAsync("/api/booking/missing/public-salon", ct);
    Assert.Equal(HttpStatusCode.NotFound, afterReset.StatusCode);
  }

  private static async Task AssertRateLimitProblem(HttpResponseMessage response, CancellationToken ct)
  {
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken: ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.RateLimitExceeded, problem.ErrorCode);
  }

  private static void AssertHasRetryAfterHeader(HttpResponseMessage response)
  {
    Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfterValues));
    Assert.NotNull(retryAfterValues);
    Assert.NotEmpty(retryAfterValues);
  }

  private sealed record ProblemDetailsResponse(string? ErrorCode);
}
