using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// APPT — POST /api/Appointments/swap + GET /api/Appointments/swap/preview (HTTP-level).
/// </summary>
public sealed class AppointmentSwapIntegrationTests
{
  [Fact]
  public async Task Swap_equal_duration_returns_ok_and_persists_exchanged_slots()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var firstId = SeedAppointment(factory.Services, seed, TestDates.InDays(20), new TimeOnly(10, 0), 30);
    var secondId = SeedAppointment(factory.Services, seed, TestDates.InDays(20), new TimeOnly(12, 0), 30);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsync(
      "/api/Appointments/swap",
      JsonContent.Create(new { firstAppointmentId = firstId, secondAppointmentId = secondId, harmonizeToShorter = false }),
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var first = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == firstId, ct);
    var second = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == secondId, ct);

    Assert.Equal(new TimeOnly(12, 0), first.StartTime);
    Assert.Equal(new TimeOnly(10, 0), second.StartTime);
  }

  [Fact]
  public async Task Preview_equal_duration_returns_expected_flags()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var firstId = SeedAppointment(factory.Services, seed, TestDates.InDays(22), new TimeOnly(10, 0), 30);
    var secondId = SeedAppointment(factory.Services, seed, TestDates.InDays(22), new TimeOnly(12, 0), 30);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync(
      $"/api/Appointments/swap/preview?firstId={firstId}&secondId={secondId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var dto = await response.Content.ReadFromJsonAsync<SwapPreviewResponse>(ct);
    Assert.NotNull(dto);
    Assert.True(dto!.EqualDuration);
    Assert.True(dto.PlainSwapFits);
    Assert.False(dto.HarmonizationAvailable);
  }

  [Fact]
  public async Task Swap_unequal_duration_harmonize_shortens_longer_and_persists()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var longServiceId = SeedLongService(factory.Services, seed, durationMinutes: 60, price: 150m);

    var longId = SeedAppointment(factory.Services, seed, TestDates.InDays(24), new TimeOnly(10, 0), 60, longServiceId);
    var shortId = SeedAppointment(factory.Services, seed, TestDates.InDays(24), new TimeOnly(13, 0), 30);
    // Blocker tuż po krótkiej wizycie — plain swap (60 min na 13:00) nie zmieści się.
    SeedAppointment(factory.Services, seed, TestDates.InDays(24), new TimeOnly(13, 45), 30);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Bez harmonizacji — nie mieści się.
    var plain = await ownerClient.PostAsync(
      "/api/Appointments/swap",
      JsonContent.Create(new { firstAppointmentId = longId, secondAppointmentId = shortId, harmonizeToShorter = false }),
      ct);
    Assert.NotEqual(HttpStatusCode.OK, plain.StatusCode);

    // Z harmonizacją — dłuższa wizyta przyjmuje krótszą usługę i skraca się.
    var harmonized = await ownerClient.PostAsync(
      "/api/Appointments/swap",
      JsonContent.Create(new { firstAppointmentId = longId, secondAppointmentId = shortId, harmonizeToShorter = true }),
      ct);
    Assert.Equal(HttpStatusCode.OK, harmonized.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var longAppt = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == longId, ct);

    Assert.Equal(seed.ServiceId, longAppt.ServiceId);     // przyjęła krótszą usługę
    Assert.Equal(new TimeOnly(13, 0), longAppt.StartTime);
    Assert.Equal(new TimeOnly(13, 30), longAppt.EndTime); // skrócona do 30 min
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices,
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime,
    int durationMinutes,
    Guid? serviceId = null)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = new Appointment(
      seed.TenantId, seed.EmployeeId, serviceId ?? seed.ServiceId, seed.CustomerId,
      date, startTime, startTime.AddMinutes(durationMinutes),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }

  private static Guid SeedLongService(IServiceProvider rootServices, RestApiIntegrationSeedResult seed, int durationMinutes, decimal price)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var service = new Service(seed.TenantId, seed.ServiceCategoryId, seed.VatRateId, "Koloryzacja", new Money(price, "PLN"), durationMinutes);
    db.Services.Add(service);

    var employee = db.Employees.IgnoreQueryFilters().Include(e => e.Services).First(e => e.Id == seed.EmployeeId);
    employee.AssignService(seed.TenantId, service.Id, service.DurationInMinutes, new Money(price, "PLN"));

    db.SaveChanges();
    return service.Id;
  }

  private sealed record SwapPreviewResponse(
    bool EqualDuration,
    bool PlainSwapFits,
    bool HarmonizationAvailable,
    bool HarmonizedSwapFits,
    Guid? ServiceChangeAppointmentId,
    string? FromServiceName,
    string? ToServiceName,
    decimal? OldPrice,
    decimal? NewPrice);
}
