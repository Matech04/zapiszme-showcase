using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Globalny tryb serwisowy platformy (kill-switch). Włączany przez admina, egzekwowany przez
/// <c>PlatformMaintenanceMiddleware</c>: odczyty przechodzą, zapisy poza adminem platformy → 503.
/// </summary>
public sealed class PlatformMaintenanceIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Maintenance_blocks_owner_writes_but_allows_reads_and_admin_toggle()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var admin = factory.CreateAdminClient();
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Admin włącza globalny tryb serwisowy.
    var enable = await admin.PutAsJsonAsync(
      "/api/admin/system/maintenance", new { enabled = true, message = "Prace platformy" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

    // Odczyt ownera nadal działa (GET nie jest blokowany).
    var read = await owner.GetAsync("/api/SalonSettings", ct);
    Assert.Equal(HttpStatusCode.OK, read.StatusCode);

    // Zapis ownera jest zablokowany 503 z errorCode = platform.maintenance.
    var blockedWrite = await owner.PutAsJsonAsync(
      "/api/SalonSettings/booking-pause", new { paused = true }, ct);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, blockedWrite.StatusCode);
    var problem = await blockedWrite.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonRead, ct);
    Assert.Equal("platform.maintenance", problem?.ErrorCode);

    // Admin platformy może wyłączyć tryb (endpoint na allowliście).
    var disable = await admin.PutAsJsonAsync(
      "/api/admin/system/maintenance", new { enabled = false }, ct);
    Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

    // Po wyłączeniu zapis ownera znów przechodzi.
    var okWrite = await owner.PutAsJsonAsync(
      "/api/SalonSettings/booking-pause", new { paused = false }, ct);
    Assert.Equal(HttpStatusCode.NoContent, okWrite.StatusCode);
  }

  [Fact]
  public async Task Maintenance_status_reflects_toggle()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var admin = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var initial = await admin.GetFromJsonAsync<MaintenanceStatusResponse>(
      "/api/admin/system/maintenance", JsonRead, ct);
    Assert.NotNull(initial);
    Assert.False(initial!.Enabled);

    await admin.PutAsJsonAsync(
      "/api/admin/system/maintenance", new { enabled = true, message = "Prace" }, ct);

    var afterEnable = await admin.GetFromJsonAsync<MaintenanceStatusResponse>(
      "/api/admin/system/maintenance", JsonRead, ct);
    Assert.NotNull(afterEnable);
    Assert.True(afterEnable!.Enabled);
    Assert.Equal("Prace", afterEnable.Message);
  }

  private sealed record ProblemDetailsResponse(string? ErrorCode);

  private sealed record MaintenanceStatusResponse(bool Enabled, string? Message, DateTime? StartedAtUtc);
}
