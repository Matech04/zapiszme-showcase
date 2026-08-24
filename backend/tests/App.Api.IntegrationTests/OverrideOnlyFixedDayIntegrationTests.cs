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
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// „Papierowy kalendarz": pracownik BEZ grafiku tygodniowego (tryb globalny domyślny = Grid),
/// ale z wyjątkiem stałogodzinnym na konkretny dzień. Tryb per-dzień sprawia, że ten dzień jest
/// stały: available-slots zwraca dokładnie godziny z wyjątku, a hold online egzekwuje sloty.
/// </summary>
public sealed class OverrideOnlyFixedDayIntegrationTests
{
  private const string Slug = "integration-override-only";

  // Data LICZONA, nie zaszyta — stała „2026-08-04" zamieniała ten test w bombę zegarową
  // (po tej dacie termin jest przeszły → brak slotów → 400 na holdzie). Wyjątek grafiku
  // zakładamy dokładnie na ten sam dzień, więc jedno źródło prawdy dla obu miejsc.
  private static readonly DateOnly BookingDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
  private static readonly string BookingDate = BookingDay.ToString("yyyy-MM-dd");

  private static void Seed(IServiceProvider rootServices, out Guid employeeId, out Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Integration Override Only", Slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Ala", "Nails", "ala@nails.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    // BRAK grafiku tygodniowego — tryb pracownika zostaje domyślny (Grid).
    // Tylko wyjątek stałogodzinny na dzień rezerwacji.
    employee.SetScheduleOverride(BookingDay,
      new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }));

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
  public async Task Available_slots_come_from_fixed_override_without_weekly_schedule()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    Seed(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync(
      $"/api/booking/{Slug}/appointments/available-slots?date={BookingDate}&employeeId={employeeId}&serviceIds={serviceId}",
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var slots = await response.Content.ReadFromJsonAsync<List<SlotDto>>(
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    Assert.NotNull(slots);
    Assert.Equal(new[] { "09:00", "12:00", "15:00" }, slots!.Select(s => s.slot).ToArray());
  }

  [Fact]
  public async Task Hold_off_override_slot_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    Seed(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new { serviceIds = new[] { serviceId }, employeeId, date = BookingDate, startTime = "09:30:00" },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Hold_on_override_slot_succeeds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    Seed(factory.Services, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new { serviceIds = new[] { serviceId }, employeeId, date = BookingDate, startTime = "12:00:00" },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private sealed record SlotDto(string slot, bool isPreferred);
}
