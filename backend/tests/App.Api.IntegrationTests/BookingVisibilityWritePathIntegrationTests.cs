using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Widoczność terminu na ścieżce ZAPISU: tworzenie rezerwacji i przekładanie terminu.
///
/// Powód powstania: reguła `IsDateOpenForOnlineBooking` była wołana wyłącznie z trzech zapytań
/// ODCZYTU, więc `available-slots` poprawnie ukrywał zamknięty miesiąc, a `POST /hold` na ten sam
/// termin i tak tworzył wizytę. Panel deklarował „zapisy zamknięte", a rezerwacje wchodziły.
/// Poprzedni zestaw testów widoczności (11 przypadków) w całości dotyczył odczytu i tego nie złapał
/// — te testy istnieją po to, żeby asymetria read/write nie wróciła.
/// </summary>
public sealed class BookingVisibilityWritePathIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record HoldResponse(Guid AppointmentId, HoldLeaseDto Lease);
  private sealed record HoldLeaseDto(Guid ReservationToken, DateTime ExpiryTimeUtc);

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

  private static Task<HttpResponseMessage> PostHoldAsync(
    HttpClient client, Guid employeeId, Guid serviceId, DateOnly date, CancellationToken ct) =>
    client.PostAsJsonAsync(
      $"/api/booking/{RestApiIntegrationSeed.TenantSlug}/public-appointment/hold",
      new
      {
        employeeId,
        serviceIds = new[] { serviceId },
        date = date.ToString("yyyy-MM-dd"),
        startTime = "10:00:00",
      },
      ct);

  // ── tworzenie rezerwacji ──────────────────────────────────────────────────

  [Fact]
  public async Task Hold_beyond_booking_horizon_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);

    var response = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, beyondHorizon, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Hold_in_month_closed_indefinitely_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Dzień dobrze wewnątrz horyzontu — blokuje go wyłącznie decyzja salonu.
    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    SetMonthPublication(factory.Services, seed.EmployeeId, target.Year, target.Month, null);

    var client = factory.CreateClient();
    var response = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, target, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Hold_in_month_closed_until_future_date_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    SetMonthPublication(
      factory.Services, seed.EmployeeId, target.Year, target.Month,
      DateOnly.FromDateTime(DateTime.UtcNow).AddDays(35));

    var client = factory.CreateClient();
    var response = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, target, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Hold_inside_horizon_in_open_month_still_succeeds()
  {
    // Kontrola negatywna: bez niej testy wyżej przechodziłyby także wtedy, gdyby endpoint
    // był po prostu zepsuty i odrzucał wszystko.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var insideHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    var response = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, insideHorizon, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Explicit_publication_allows_hold_beyond_the_horizon()
  {
    // Wiersz miesiąca nadpisuje horyzont także w górę — „otwieramy grudzień już teraz, bo święta".
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);
    SetMonthPublication(
      factory.Services, seed.EmployeeId, beyondHorizon.Year, beyondHorizon.Month,
      DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

    var client = factory.CreateClient();
    var response = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, beyondHorizon, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  // ── przekładanie terminu ──────────────────────────────────────────────────

  [Fact]
  public async Task Patching_a_hold_into_a_closed_month_is_rejected()
  {
    // Bez tej bramki łatka na tworzenie rezerwacji obchodzi się jednym PATCH-em: hold zakładany
    // na dozwolony dzień, a potem przesuwany do zamkniętego miesiąca.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var allowed = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    // `closed` MUSI wypaść w INNYM miesiącu niż `allowed`. Wcześniej było to `+40 dni`, co
    // trzymało się tylko przez część roku: gdy dziś wypada na początku miesiąca (np. 6 sierpnia),
    // +30 i +40 lądują w tym samym miesiącu, więc zamknięcie miesiąca zamykało też dzień holdu
    // i test padał na PIERWSZYM kroku — zanim w ogóle doszło do sprawdzanego PATCH-a.
    // Dziesiąty dzień kolejnego miesiąca: zawsze inny miesiąc, zawsze w horyzoncie (≤ ~70 dni).
    var closed = new DateOnly(allowed.Year, allowed.Month, 1).AddMonths(1).AddDays(9);
    SetMonthPublication(factory.Services, seed.EmployeeId, closed.Year, closed.Month, null);

    var client = factory.CreateClient();

    var holdResponse = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, allowed, ct);
    Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);
    var hold = await holdResponse.Content.ReadFromJsonAsync<HoldResponse>(JsonRead, ct);
    Assert.NotNull(hold);

    var patchResponse = await client.PatchAsJsonAsync(
      $"/api/booking/{RestApiIntegrationSeed.TenantSlug}/public-appointment/{hold.AppointmentId}",
      new
      {
        token = hold.Lease.ReservationToken,
        serviceIds = new[] { seed.ServiceId },
        employeeId = seed.EmployeeId,
        date = closed.ToString("yyyy-MM-dd"),
        startTime = "11:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);

    // Wizyta musi zostać na pierwotnym, dozwolonym terminie.
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(a => a.Id == hold.AppointmentId, ct);
    Assert.Equal(allowed, appt.Date);
  }

  [Fact]
  public async Task Patching_a_hold_beyond_the_horizon_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var allowed = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);

    var client = factory.CreateClient();

    var holdResponse = await PostHoldAsync(client, seed.EmployeeId, seed.ServiceId, allowed, ct);
    var hold = await holdResponse.Content.ReadFromJsonAsync<HoldResponse>(JsonRead, ct);
    Assert.NotNull(hold);

    var patchResponse = await client.PatchAsJsonAsync(
      $"/api/booking/{RestApiIntegrationSeed.TenantSlug}/public-appointment/{hold.AppointmentId}",
      new
      {
        token = hold.Lease.ReservationToken,
        serviceIds = new[] { seed.ServiceId },
        employeeId = seed.EmployeeId,
        date = beyondHorizon.ToString("yyyy-MM-dd"),
        startTime = "11:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
  }

  // ── granica panel / klient ────────────────────────────────────────────────

  [Fact]
  public async Task Staff_can_still_book_in_a_closed_month()
  {
    // Sedno rozdziału: publikacja miesiąca wyłącza SPRZEDAŻ online, nie grafik. Personel wpisuje
    // wizyty w zamkniętym miesiącu normalnie — inaczej funkcja blokowałaby pracę salonu.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var closed = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    SetMonthPublication(factory.Services, seed.EmployeeId, closed.Year, closed.Month, null);

    var client = factory.CreateOwnerClient();

    var response = await client.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = closed.ToString("yyyy-MM-dd"),
        startTime = "12:00:00",
        customerId = seed.CustomerId,
        source = AppointmentSource.Panel,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Staff_can_still_book_beyond_the_booking_horizon()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var beyondHorizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);
    var client = factory.CreateOwnerClient();

    var response = await client.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = beyondHorizon.ToString("yyyy-MM-dd"),
        startTime = "12:00:00",
        customerId = seed.CustomerId,
        source = AppointmentSource.Panel,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }
}
