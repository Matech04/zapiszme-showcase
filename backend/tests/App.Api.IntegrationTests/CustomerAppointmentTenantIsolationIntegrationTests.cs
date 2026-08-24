using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Hardening tests for strict tenant isolation on customer and appointment endpoints.
/// </summary>
public sealed class CustomerAppointmentTenantIsolationIntegrationTests
{
  [Fact]
  public async Task Owner_cannot_get_or_update_customer_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var getResponse = await ownerClient.GetAsync($"/api/Customers/{second.CustomerId}", ct);
    var updateResponse = await ownerClient.PutAsJsonAsync(
      $"/api/Customers/{second.CustomerId}",
      new
      {
        id = second.CustomerId,
        firstName = "Cross",
        lastName = "Tenant",
        email = "cross@tenant.local",
        phoneNumber = "+48509990099",
        generalNotes = "x",
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_delete_customer_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.DeleteAsync($"/api/Customers/{second.CustomerId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_create_appointment_with_customer_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = own.EmployeeId,
        serviceIds = new[] { own.ServiceId },
        // Data LICZONA: przy zaszytej „2026-08-03" po tym dniu wracało 400 (termin przeszły)
        // zamiast 404 — test przestawał sprawdzać izolację tenantów i zaczynał sprawdzać kalendarz.
        date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd"),
        startTime = "10:00:00",
        customerId = second.CustomerId,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Owner_cannot_get_or_delete_appointment_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var secondAppointmentId = SeedAppointment(factory.Services, second, TestDates.InDays(14), new TimeOnly(11, 0));
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var getResponse = await ownerClient.GetAsync($"/api/Appointments/{secondAppointmentId}", ct);
    var deleteResponse = await ownerClient.DeleteAsync($"/api/Appointments/{secondAppointmentId}", ct);

    Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
  }

  [Fact]
  public async Task Appointment_list_is_isolated_per_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ownAppointmentId = SeedAppointment(factory.Services, own, TestDates.InDays(15), new TimeOnly(9, 0));
    var secondAppointmentId = SeedAppointment(factory.Services, second, TestDates.InDays(15), new TimeOnly(10, 0));
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync(
      $"/api/Appointments?startDate={TestDates.IsoInDays(0)}&endDate={TestDates.IsoInDays(30)}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<AppointmentListItem>>(cancellationToken: ct);
    Assert.NotNull(list);
    Assert.Contains(list, a => a.Id == ownAppointmentId);
    Assert.DoesNotContain(list, a => a.Id == secondAppointmentId);
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices,
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appointment = new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      seed.CustomerId,
      date,
      startTime,
      startTime.AddMinutes(30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appointment);
    db.SaveChanges();
    return appointment.Id;
  }

  private sealed record AppointmentListItem(Guid Id);
}
