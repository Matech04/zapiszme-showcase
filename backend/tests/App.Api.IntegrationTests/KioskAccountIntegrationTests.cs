using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Faza 3 — konto „Recepcja" (kiosk): provisioning przez właściciela, ukrycie z listy pracowników
/// (IsBookable=false), oraz granice autoryzacji — kiosk obsługuje wizyty całego zespołu, ale nie ma
/// dostępu do ustawień/pracowników/subskrypcji.
/// </summary>
public sealed class KioskAccountIntegrationTests
{
  private sealed record KioskStatus(bool Exists, string? Email);
  private sealed record EmpRow(Guid Id, string Email);

  // ── Provisioning ─────────────────────────────────────────────────────────────

  [Fact]
  public async Task Owner_can_create_kiosk_and_it_is_hidden_from_employees()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var create = await owner.PostAsJsonAsync(
      "/api/auth/kiosk", new { email = "recepcja@rest-seed.local", password = "KioskPass123!" }, ct);
    Assert.Equal(HttpStatusCode.OK, create.StatusCode);

    var status = await owner.GetFromJsonAsync<KioskStatus>("/api/auth/kiosk", ct);
    Assert.NotNull(status);
    Assert.True(status!.Exists);
    Assert.Equal("recepcja@rest-seed.local", status.Email);

    // Kiosk nie jest rezerwowalnym specjalistą → nie pojawia się na liście zespołu/kalendarza.
    var employees = await owner.GetFromJsonAsync<List<EmpRow>>("/api/Employees", ct);
    Assert.DoesNotContain(employees!, e => e.Email == "recepcja@rest-seed.local");
  }

  [Fact]
  public async Task Create_kiosk_twice_returns_conflict()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var first = await owner.PostAsJsonAsync(
      "/api/auth/kiosk", new { email = "recepcja@rest-seed.local", password = "KioskPass123!" }, ct);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);

    var second = await owner.PostAsJsonAsync(
      "/api/auth/kiosk", new { email = "recepcja2@rest-seed.local", password = "KioskPass123!" }, ct);
    Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
  }

  [Fact]
  public async Task Owner_can_reset_kiosk_password()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    await owner.PostAsJsonAsync(
      "/api/auth/kiosk", new { email = "recepcja@rest-seed.local", password = "KioskPass123!" }, ct);

    var reset = await owner.PostAsJsonAsync(
      "/api/auth/kiosk/password", new { password = "NewKioskPass456!" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
  }

  [Fact]
  public async Task Creating_kiosk_requires_owner_returns_forbidden_for_manager()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var manager = factory.CreateManagerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await manager.PostAsJsonAsync(
      "/api/auth/kiosk", new { email = "recepcja@rest-seed.local", password = "KioskPass123!" }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // ── Kiosk authorization boundaries ───────────────────────────────────────────

  [Fact]
  public async Task Kiosk_can_read_appointments_for_any_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var kiosk = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    // Kiosk pyta o kalendarz dowolnego pracownika — bypass StaffCalendarVisibilityPolicy
    // (zwykły Employee z OwnCalendarOnly dostałby tu 403).
    var response = await kiosk.GetAsync(
      $"/api/Appointments?employeeId={Guid.NewGuid()}&startDate={TestDates.IsoInDays(0)}&endDate={TestDates.IsoInDays(7)}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_cannot_create_employee_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var kiosk = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await kiosk.PostAsJsonAsync(
      "/api/Employees", new { firstName = "X", lastName = "Y", email = "x@y.local" }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_cannot_write_salon_settings_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var kiosk = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await kiosk.PutAsJsonAsync("/api/SalonSettings", new { name = "Hacked" }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_cannot_manage_kiosk_account_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var kiosk = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    // Provisioning konta recepcji to BusinessManagement — sam kiosk nie może się „rozmnażać".
    var response = await kiosk.GetAsync("/api/auth/kiosk", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }
}
