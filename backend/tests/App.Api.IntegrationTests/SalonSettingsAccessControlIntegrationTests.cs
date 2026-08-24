using System.Net;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// B2 (preflight MEDIUM): GET /api/SalonSettings jest dostępny dla całego personelu (kalendarz
/// czyta StaffCalendarVisibilityPolicy), ale konfiguracja płatności/zadatków należy do właściciela.
/// Pracownik (rola Employee) NIE może widzieć DepositSettings ani MerchantAccount; Owner — owszem.
/// </summary>
public sealed class SalonSettingsAccessControlIntegrationTests
{
  private static bool HasObject(JsonElement root, string name) =>
    root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object;

  // API-SALON-SETTINGS-001: Owner widzi pełną konfigurację (deposit + merchant).
  [Fact]
  public async Task Owner_get_salon_settings_includes_deposit_and_merchant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync("/api/SalonSettings", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var root = doc.RootElement;
    Assert.True(HasObject(root, "depositSettings"), "Owner powinien widzieć depositSettings.");
    Assert.True(HasObject(root, "merchantAccount"), "Owner powinien widzieć merchantAccount.");
  }

  // API-SALON-SETTINGS-002: Employee dostaje pola operacyjne, ale BEZ deposit/merchant.
  [Fact]
  public async Task Employee_get_salon_settings_omits_deposit_and_merchant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync("/api/SalonSettings", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var root = doc.RootElement;

    // Pola wrażliwe (płatności) odcięte dla Employee — nieobecne albo null, nigdy obiekt.
    Assert.False(HasObject(root, "depositSettings"), "Employee NIE powinien widzieć depositSettings.");
    Assert.False(HasObject(root, "merchantAccount"), "Employee NIE powinien widzieć merchantAccount.");

    // …a operacyjne pola DTO nadal są (GET nie jest po prostu zablokowany).
    Assert.Equal(RestApiIntegrationSeed.TenantSlug, root.GetProperty("slug").GetString());
  }
}
