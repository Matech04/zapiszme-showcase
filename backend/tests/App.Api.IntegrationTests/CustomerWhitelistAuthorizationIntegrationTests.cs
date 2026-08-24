using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Whitelist zdejmuje z klienta wymóg zadatku i weryfikacji — to decyzja o pieniądzach salonu,
/// nie o obsłudze wizyty. Wcześniej siedziała na `GeneralAccess`, więc mógł ją przestawić każdy
/// zalogowany, łącznie z kontem Recepcji. Teraz minimum Manager (`StaffManagement`).
/// Eksport RODO pokrywa `CustomerExportAuthorizationIntegrationTests`.
/// </summary>
public sealed class CustomerWhitelistAuthorizationIntegrationTests
{
  // ── Whitelist: minimum Manager ───────────────────────────────────────────────────────────

  [Fact]
  public async Task Manager_can_set_whitelist()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateManagerClient()
      .PutAsJsonAsync($"/api/Customers/{seed.CustomerId}/whitelist", new { isWhitelisted = true }, ct);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_set_whitelist()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateEmployeeClient()
      .PutAsJsonAsync($"/api/Customers/{seed.CustomerId}/whitelist", new { isWhitelisted = true }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_bulk_whitelist()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateEmployeeClient()
      .PostAsJsonAsync("/api/Customers/whitelist/bulk",
        new { customerIds = new[] { seed.CustomerId }, isWhitelisted = true }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_cannot_set_whitelist()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateKioskClient()
      .PutAsJsonAsync($"/api/Customers/{seed.CustomerId}/whitelist", new { isWhitelisted = true }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // ── Klienci: obsługa wizyty zostaje na GeneralAccess ─────────────────────────────────────

  [Fact]
  public async Task Employee_can_still_read_customers()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var response = await factory.CreateEmployeeClient().GetAsync("/api/Customers", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }
}
