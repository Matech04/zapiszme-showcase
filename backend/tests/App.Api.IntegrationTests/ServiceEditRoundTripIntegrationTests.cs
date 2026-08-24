using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Application.Services.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// SVC-018 — kontrakt edycji usługi w panelu.
///
/// Regresja: GET /api/Services/{id} zwracał DTO bez VatRateId. Formularz w dashboardzie
/// mapował data.vatRateId → undefined, klient nie był w stanie odesłać prawidłowego
/// UpdateServiceCommand (VatRateId jest wymagany przez FluentValidation), a użytkownik
/// widział „nie da się edytować czasu usługi”.
///
/// Te testy wymuszają, żeby payload zwracany przez GET dało się odesłać bez zmian
/// jako PUT i zwracał 204 — czyli kontrakt read-modify-write zamyka się sam.
/// </summary>
public sealed class ServiceEditRoundTripIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public async Task GET_service_returns_VatRateId()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/Services/{seed.ServiceId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var dto = await response.Content.ReadFromJsonAsync<ServiceDto>(JsonOptions, ct);
    Assert.NotNull(dto);
    Assert.Equal(seed.VatRateId, dto!.VatRateId);
  }

  // Pełen flow z panelu: pobierz usługę, zmień durationInMinutes, wyślij to samo
  // payload z powrotem przez PUT. Bez VatRateId w GET, ten test rzuca 400 (FluentValidation).
  [Fact]
  public async Task GET_then_PUT_with_changed_duration_persists_change()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var getResponse = await client.GetAsync($"/api/Services/{seed.ServiceId}", ct);
    Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    var current = await getResponse.Content.ReadFromJsonAsync<ServiceDto>(JsonOptions, ct);
    Assert.NotNull(current);

    var putPayload = new
    {
      id = current!.Id,
      categoryId = current.CategoryId,
      vatRateId = current.VatRateId,
      name = current.Name,
      amount = current.Price.Amount,
      currency = current.Price.Currency,
      durationInMinutes = current.DurationInMinutes + 15,
    };

    var putResponse = await client.PutAsJsonAsync($"/api/Services/{seed.ServiceId}", putPayload, ct);

    Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var updated = await db.Services
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(s => s.Id == seed.ServiceId, ct);
    Assert.NotNull(updated);
    Assert.Equal(current.DurationInMinutes + 15, updated!.DurationInMinutes);
    Assert.Equal(seed.VatRateId, updated.VatRateId);
  }

  // Regresja prod: przypisanie pracownika z multi-selecta w formularzu usługi NIE może robić
  // snapshotu czasu/ceny — ma dziedziczyć z katalogu (null), żeby edycja czasu usługi propagowała
  // się do nowych wizyt. Weryfikuje też round-trip nullowalnej kolumny custom_duration na realnym PG.
  [Fact]
  public async Task PUT_service_with_employees_assigns_inheriting_null_override()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Drugi pracownik (bez przypisanej usługi). Dodany poza kontekstem tenanta — write-check
    // TenantViolation jest pomijany, gdy ICurrentTenantService.TenantId == null (jak w seederze).
    Guid newEmployeeId;
    using (var seedScope = factory.Services.CreateScope())
    {
      var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var emp = new Employee(seed.TenantId, null, "Bea", "Stylist", "bea@rest-seed.local");
      seedDb.Employees.Add(emp);
      await seedDb.SaveChangesAsync(ct);
      newEmployeeId = emp.Id;
    }

    var getResponse = await client.GetAsync($"/api/Services/{seed.ServiceId}", ct);
    var current = await getResponse.Content.ReadFromJsonAsync<ServiceDto>(JsonOptions, ct);
    Assert.NotNull(current);
    var newDuration = current!.DurationInMinutes + 5;

    // PUT jak z panelu: zmieniony czas + lista pracowników (multi-select w formularzu usługi).
    var putPayload = new
    {
      id = current.Id,
      categoryId = current.CategoryId,
      vatRateId = current.VatRateId,
      name = current.Name,
      amount = current.Price.Amount,
      currency = current.Price.Currency,
      durationInMinutes = newDuration,
      employeeIds = new[] { seed.EmployeeId, newEmployeeId },
    };
    var putResponse = await client.PutAsJsonAsync($"/api/Services/{seed.ServiceId}", putPayload, ct);
    Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var service = await db.Services.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(s => s.Id == seed.ServiceId, ct);
    Assert.Equal(newDuration, service.DurationInMinutes);

    var newEmp = await db.Employees.IgnoreQueryFilters().AsNoTracking()
      .Include(e => e.Services)
      .FirstAsync(e => e.Id == newEmployeeId, ct);
    var link = Assert.Single(newEmp.Services);
    Assert.Equal(seed.ServiceId, link.ServiceId);
    Assert.Null(link.CustomDuration);
    Assert.Null(link.CustomPrice);
    // Czas rozwiązany dla wizyty = żywy czas katalogu (nie stary snapshot).
    Assert.Equal(newDuration, newEmp.ResolveServiceDurationMinutes(link.ServiceId, service.DurationInMinutes));
  }

  // Sanity: GET /api/Services (listing) też musi zwracać VatRateId — formularz katalogu
  // może go potrzebować przy widokach grupowania/filtrowania.
  [Fact]
  public async Task GET_services_list_returns_VatRateId_for_each_item()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Services", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<ServiceDto>>(JsonOptions, ct);
    Assert.NotNull(list);
    Assert.NotEmpty(list!);
    Assert.All(list, dto => Assert.Equal(seed.VatRateId, dto.VatRateId));
  }
}
