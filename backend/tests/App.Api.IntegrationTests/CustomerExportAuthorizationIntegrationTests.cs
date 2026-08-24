using System.Net;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// CUST-011 — GET /api/Customers/{id}/export jest Owner-only (BusinessManagement).
///
/// Regresja z bramki preflight 2026-07-09: akcja dziedziczyła `GeneralAccess` z kontrolera, więc pełny
/// zrzut PII klienta (RODO art. 15/20) mógł pobrać szeregowy Employee, a po dodaniu roli Kiosk także
/// współdzielony terminal recepcji. `GetCustomers` (również GeneralAccess) wydaje listę GUID-ów, więc
/// eksport był enumerowalny — eksfiltracja masowa, nie punktowa. Bliźniaczy `anonymize` był Owner-only
/// od początku; ten test pilnuje, żeby obie operacje RODO zostały na tym samym poziomie uprawnień.
/// </summary>
public sealed class CustomerExportAuthorizationIntegrationTests
{
  [Fact]
  public async Task Owner_can_export_customer_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();

    var response = await client.GetAsync(
      $"/api/Customers/{seed.CustomerId}/export",
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Manager_cannot_export_customer_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateManagerClient();

    var response = await client.GetAsync(
      $"/api/Customers/{seed.CustomerId}/export",
      TestContext.Current.CancellationToken);

    // Manager NIE jest w BusinessManagement (tylko Owner/Admin) — spójnie z anonymize.
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Employee_cannot_export_customer_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();

    var response = await client.GetAsync(
      $"/api/Customers/{seed.CustomerId}/export",
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_cannot_export_customer_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateKioskClient();

    var response = await client.GetAsync(
      $"/api/Customers/{seed.CustomerId}/export",
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }
}
