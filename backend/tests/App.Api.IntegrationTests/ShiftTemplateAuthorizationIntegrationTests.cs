using System.Net;
using App.Api.E2eSupport;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Szablony zmian są zasobem współdzielonym całego salonu (brak `EmployeeId`, twarde usuwanie),
/// więc CAŁY kontroler — łącznie z odczytem — wymaga `StaffManagement`. Employee i Kiosk nie
/// widzą szablonów; Owner i Manager tak.
/// </summary>
public sealed class ShiftTemplateAuthorizationIntegrationTests
{
  [Fact]
  public async Task Employee_role_cannot_read_shift_templates_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ShiftTemplates", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Kiosk_role_cannot_read_shift_templates_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ShiftTemplates", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Manager_role_can_read_shift_templates()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateManagerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ShiftTemplates", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Owner_role_can_read_shift_templates()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/ShiftTemplates", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }
}
