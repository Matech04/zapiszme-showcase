using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// End-to-end (Testcontainers Postgres) dla trybu stałych slotów: weryfikuje że nowa migracja
/// (kolumna tablicy `fixed_start_times` + `slot_generation_mode`) działa na realnej bazie,
/// publiczne available-slots zwraca dokładnie wpisane godziny, a publiczny hold poza slotem
/// jest odrzucany (400), na slocie — przyjęty.
/// </summary>
public sealed class FixedStartTimesBookingIntegrationTests
{
  private const string Slug = "integration-fixed-salon";
  // Data LICZONA, nie zaszyta. Stała „2026-08-04" była bombą zegarową: po tym dniu booking
  // odrzucał termin jako przeszły, `available-slots` zwracało pustą listę i wszystkie trzy
  // przypadki tej klasy padały — bez związku ze zmianą w kodzie produkcyjnym.
  // Grafik fixed zakładamy na WSZYSTKIE dni tygodnia, więc dzień tygodnia nie ma tu znaczenia.
  private static readonly string BookingDate =
    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");

  private static void SeedFixedSalon(IServiceProvider rootServices, out Guid employeeId, out Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Integration Fixed Salon", Slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Ala", "Nails", "ala@nails.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    var days = Enum.GetValues<DayOfWeek>()
      .Select(d => new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }, cycleIndex: (int)d))
      .ToList();
    employee.SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days);

    employeeId = employee.Id;
    serviceId = service.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.SaveChanges();
  }

  [Fact]
  public async Task Available_slots_returns_exactly_fixed_times_and_persists_through_real_postgres()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedFixedSalon(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/booking/{Slug}/appointments/available-slots?date={BookingDate}&employeeId={employeeId}&serviceIds={serviceId}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var slots = await response.Content.ReadFromJsonAsync<List<SlotDto>>(jsonOptions, ct);
    Assert.NotNull(slots);
    Assert.Equal(new[] { "09:00", "12:00", "15:00" }, slots!.Select(s => s.slot).ToArray());
  }

  [Fact]
  public async Task Hold_off_slot_is_rejected_with_400()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedFixedSalon(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new { serviceIds = new[] { serviceId }, employeeId, date = BookingDate, startTime = "09:30:00" },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken: ct);
    Assert.NotNull(problem);
    Assert.Equal("appointment.slot_unavailable", problem!.ErrorCode);
  }

  [Fact]
  public async Task Hold_on_slot_succeeds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedFixedSalon(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new { serviceIds = new[] { serviceId }, employeeId, date = BookingDate, startTime = "09:00:00" },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private sealed record SlotDto(string slot, bool isPreferred);

  private sealed record ProblemDetailsResponse(string? ErrorCode);
}
