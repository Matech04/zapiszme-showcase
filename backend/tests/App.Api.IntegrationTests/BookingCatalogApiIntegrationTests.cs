using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>Publiczny katalog booking (<c>AllowAnonymous</c>) poza OTP z poprzednich testów.</summary>
public sealed class BookingCatalogApiIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Get_booking_employees_without_serviceId_returns_all_bookable()
  {
    // Lejek klienta zaczyna od wyboru pracownika (przed usługą) → brak serviceId zwraca wszystkich
    // bookowalnych pracowników salonu, nie pustą listę.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/booking/{RestApiIntegrationSeed.TenantSlug}/employees",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<BookingEmployeeItem>>(JsonRead, ct);
    Assert.NotNull(list);
    Assert.Contains(list, e => e.Id == seed.EmployeeId);
  }

  [Fact]
  public async Task Get_booking_services_omits_service_with_no_assigned_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var orphan = new Service(
          seed.TenantId,
          seed.ServiceCategoryId,
          seed.VatRateId,
          "Tylko w katalogu",
          new Money(15m, "PLN"),
          15);
      db.Services.Add(orphan);
      db.SaveChanges();
    }

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/booking/{seed.TenantSlug}/services",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<BookingServiceItem>>(JsonRead, ct);
    Assert.NotNull(list);
    Assert.DoesNotContain(list, s => s.Name == "Tylko w katalogu");
    Assert.Contains(list, s => s.Id == seed.ServiceId);
  }

  [Fact]
  public async Task Get_booking_employees_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/booking/{seed.TenantSlug}/employees?serviceIds={seed.ServiceId}",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<BookingEmployeeItem>>(JsonRead, ct);
    Assert.NotNull(list);
    Assert.NotEmpty(list);
  }

  [Fact]
  public async Task Get_booking_services_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/booking/{seed.TenantSlug}/services",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_booking_service_categories_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
        $"/api/booking/{seed.TenantSlug}/service-categories",
        ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_booking_available_slots_returns_ok()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Grafik seeda obejmuje Pn–Nd, więc dzień tygodnia nie ma znaczenia — liczy się „w przyszłości".
    var monday = TestDates.InDays(14);
    var url =
        $"/api/booking/{seed.TenantSlug}/appointments/available-slots?date={monday:yyyy-MM-dd}&employeeId={seed.EmployeeId}&serviceIds={seed.ServiceId}";

    var response = await client.GetAsync(url, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private sealed record BookingEmployeeItem(Guid Id, string FirstName, string LastName);

  private sealed record BookingServiceItem(Guid Id, string Name);
}
