using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.CustomerAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Regresja kontraktu: POST /api/Appointments z <c>customerPhone</c> jako ZWYKŁYM STRINGIEM JSON.
/// Wcześniej pole było value-objectem <c>PhoneNumber</c> (NSwag eksportował obiekt
/// <c>{ value, countryCode, ... }</c>), a panel wysyłał goły string → System.Text.Json nie umiał
/// zbindować → 400 „nie da się dodać wizyty po samym numerze”. Te testy chodzą pełną ścieżką HTTP
/// i pilnują, że string w <c>customerPhone</c> działa i poprawnie rozwiązuje klienta.
/// </summary>
public sealed class AppointmentCreateByPhoneIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  // API-APPT-PHONE-001: string customerPhone pasujący do istniejącego klienta → 200 + link do niego.
  [Fact]
  public async Task Post_appointment_with_phone_string_matching_existing_customer_links_it()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Seedowy klient ma numer "+48501110001"; wysyłamy go w formie z formularza (ze spacjami).
    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(42),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = "+48 501 110 001",
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var appointmentId = await response.Content.ReadFromJsonAsync<Guid>(JsonOpts, ct);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appointment = await db.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(seed.CustomerId, appointment.CustomerId);
  }

  // API-APPT-PHONE-002: nowy numer (brak klienta) → 200 + utworzony klient-szkielet (Source = Manual).
  [Fact]
  public async Task Post_appointment_with_new_phone_string_creates_manual_customer()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(43),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = "+48 600 700 800",
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var appointmentId = await response.Content.ReadFromJsonAsync<Guid>(JsonOpts, ct);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appointment = await db.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == appointmentId, ct);
    Assert.NotNull(appointment.CustomerId);

    var customer = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == appointment.CustomerId, ct);
    Assert.Equal("+48600700800", customer.PhoneNumber!.Value);
    Assert.Equal(CustomerSource.Manual, customer.Source);
    Assert.Equal(seed.TenantId, customer.TenantId);
  }

  // API-APPT-PHONE-003: nieprawidłowy numer → 400 (walidacja), a nie 500.
  [Fact]
  public async Task Post_appointment_with_invalid_phone_string_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(44),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = "123",
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
