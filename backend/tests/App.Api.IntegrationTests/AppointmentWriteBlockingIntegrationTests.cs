using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Blokada zarządzania wizytami przez personel (dashboard/kalendarz) gdy:
///  - salon wstrzymał rezerwacje (per-salon <c>BookingPaused</c>) → 400 <c>booking.paused</c>
///    (egzekwowane w <c>BookingPauseBehavior</c>), oraz
///  - trwa globalny tryb serwisowy platformy → 503 <c>platform.maintenance</c>
///    (egzekwowane w <c>PlatformMaintenanceMiddleware</c>, przed pipeline).
/// </summary>
public sealed class AppointmentWriteBlockingIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private static object CreateAppointmentBody(RestApiIntegrationSeedResult seed) => new
  {
    employeeId = seed.EmployeeId,
    serviceIds = new[] { seed.ServiceId },
    date = TestDates.IsoInDays(14),
    startTime = "10:00:00",
    customerId = seed.CustomerId,
  };

  [Fact]
  public async Task Booking_pause_blocks_staff_appointment_create_but_not_salon_settings()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Salon wstrzymuje rezerwacje.
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = db.Tenants.Single(t => t.Id == seed.TenantId);
      tenant.SetBookingPause(true, null);
      db.SaveChanges();
    }

    // Zarządzanie wizytami zablokowane po stronie serwera (obejście ukrytego UI) → 400 booking.paused.
    var create = await owner.PostAsJsonAsync("/api/appointments", CreateAppointmentBody(seed), ct);
    Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    var problem = await create.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonRead, ct);
    Assert.Equal("booking.paused", problem?.ErrorCode);

    // Zakres: wstrzymanie per-salon blokuje TYLKO zapis wizyt — nie-appointmentowe zapisy (tu: wznowienie
    // rezerwacji) nadal działają, więc owner może wyjść ze stanu wstrzymania.
    var resume = await owner.PutAsJsonAsync("/api/SalonSettings/booking-pause", new { paused = false }, ct);
    Assert.Equal(HttpStatusCode.NoContent, resume.StatusCode);
  }

  [Fact]
  public async Task Platform_maintenance_blocks_staff_appointment_create()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var admin = factory.CreateAdminClient();
    var owner = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var enable = await admin.PutAsJsonAsync(
      "/api/admin/system/maintenance", new { enabled = true, message = "Prace" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

    // Middleware ubija zapis (POST) zanim dojdzie do kontrolera → 503 platform.maintenance.
    var create = await owner.PostAsJsonAsync("/api/appointments", CreateAppointmentBody(seed), ct);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
    var problem = await create.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonRead, ct);
    Assert.Equal("platform.maintenance", problem?.ErrorCode);
  }

  private sealed record ProblemDetailsResponse(string? ErrorCode);
}
