using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Widoczność terminów w PUBLICZNYM API: horyzont rezerwacji i publikacja miesiąca.
///
/// To jedyna warstwa, która widzi właściwą regresję. Wcześniej górna granica istniała wyłącznie
/// w kalendarzu Svelte (`MAX_MONTHS_AHEAD`), a backend sprawdzał tylko „nie w przeszłości" —
/// więc gołe GET na odległą datę omijało barierę UI i wystawiało terminy, których salon
/// nie zamierzał jeszcze sprzedawać. Testy jednostkowe frontu tego nie łapią z definicji.
/// </summary>
public sealed class BookingVisibilityHorizonIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record SlotItem(string Slot, bool IsPreferred);
  private sealed record MonthDay(DateOnly Date, int AvailableCount, bool IsWorkingDay);
  private sealed record MonthAvailability(bool IsClosed, DateOnly? OpensOn, List<MonthDay> Days);

  private static string SlotsUrl(string slug, DateOnly date, Guid employeeId, Guid serviceId) =>
    $"/api/booking/{slug}/appointments/available-slots?date={date:yyyy-MM-dd}&employeeId={employeeId}&serviceIds={serviceId}";

  private static string MonthUrl(string slug, int year, int month, Guid employeeId, Guid serviceId) =>
    $"/api/booking/{slug}/appointments/month-availability?year={year}&month={month}&employeeId={employeeId}&serviceIds={serviceId}";

  /// <summary>Ustawia publikację miesiąca wprost w bazie — odpowiednik decyzji salonu w panelu.</summary>
  private static void SetMonthPublication(
    IServiceProvider services, Guid employeeId, int year, int month, DateOnly? opensOn)
  {
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = db.Employees
      .IgnoreQueryFilters()
      .Include(e => e.MonthPublications)
      .First(e => e.Id == employeeId);

    employee.SetMonthPublication(year, month, opensOn);
    db.SaveChanges();
  }

  [Fact]
  public async Task Slots_beyond_booking_horizon_are_not_served_to_anonymous_client()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Domyślny horyzont to 120 dni; pracownik z seeda pracuje 8–20 codziennie, więc gdyby
    // granicy nie było, ta data zwróciłaby pełną listę slotów.
    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);

    var response = await client.GetAsync(
      SlotsUrl(RestApiIntegrationSeed.TenantSlug, beyondHorizon, seed.EmployeeId, seed.ServiceId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var slots = await response.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.NotNull(slots);
    Assert.Empty(slots);
  }

  [Fact]
  public async Task Slots_inside_booking_horizon_are_still_served()
  {
    // Kontrola negatywna: bez niej test wyżej przechodziłby także wtedy, gdyby endpoint
    // był po prostu zepsuty i nie zwracał niczego.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var insideHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    var response = await client.GetAsync(
      SlotsUrl(RestApiIntegrationSeed.TenantSlug, insideHorizon, seed.EmployeeId, seed.ServiceId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var slots = await response.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.NotNull(slots);
    Assert.NotEmpty(slots);
  }

  [Fact]
  public async Task Slots_in_month_closed_until_future_date_are_not_served()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Dzień dobrze wewnątrz horyzontu — zamyka go wyłącznie decyzja salonu.
    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    SetMonthPublication(
      factory.Services, seed.EmployeeId, target.Year, target.Month,
      DateOnly.FromDateTime(DateTime.UtcNow).AddDays(35));

    var client = factory.CreateClient();
    var response = await client.GetAsync(
      SlotsUrl(RestApiIntegrationSeed.TenantSlug, target, seed.EmployeeId, seed.ServiceId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var slots = await response.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.NotNull(slots);
    Assert.Empty(slots);
  }

  [Fact]
  public async Task Month_availability_tells_the_client_when_bookings_open()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    var opensOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(35);
    SetMonthPublication(factory.Services, seed.EmployeeId, target.Year, target.Month, opensOn);

    var client = factory.CreateClient();
    var response = await client.GetAsync(
      MonthUrl(RestApiIntegrationSeed.TenantSlug, target.Year, target.Month, seed.EmployeeId, seed.ServiceId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var month = await response.Content.ReadFromJsonAsync<MonthAvailability>(JsonRead, ct);
    Assert.NotNull(month);

    // Bez daty otwarcia klient zobaczyłby pustą siatkę i przeczytał ją jako „salon nie pracuje".
    Assert.True(month.IsClosed);
    Assert.Equal(opensOn, month.OpensOn);
    Assert.All(month.Days, d => Assert.Equal(0, d.AvailableCount));
  }

  [Fact]
  public async Task Explicit_publication_opens_a_month_beyond_the_horizon()
  {
    // Odwrotny kierunek: „otwieramy grudzień już teraz, bo święta".
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);
    SetMonthPublication(
      factory.Services, seed.EmployeeId, beyondHorizon.Year, beyondHorizon.Month,
      DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

    var client = factory.CreateClient();
    var response = await client.GetAsync(
      SlotsUrl(RestApiIntegrationSeed.TenantSlug, beyondHorizon, seed.EmployeeId, seed.ServiceId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var slots = await response.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.NotNull(slots);
    Assert.NotEmpty(slots);
  }
}
