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
/// APPT-012 — PATCH /api/Appointments/{id}/reschedule (HTTP-level happy path).
/// APPT-013 — PATCH /api/Appointments/{id}/note (HTTP-level happy path).
/// </summary>
public sealed class AppointmentRescheduleAndNoteIntegrationTests
{
  [Fact]
  public async Task Reschedule_appointment_returns_ok_and_persists_new_time()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var apptId = SeedAppointment(factory.Services, seed, TestDates.InDays(15), new TimeOnly(10, 0));

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var newDate = TestDates.IsoInDays(16);
    var newStart = "13:00:00";

    var response = await ownerClient.PatchAsync(
      $"/api/Appointments/{apptId}/reschedule",
      JsonContent.Create(new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = newDate,
        startTime = newStart,
      }),
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var reloaded = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);

    Assert.Equal(TestDates.InDays(16), reloaded.Date);
    Assert.Equal(new TimeOnly(13, 0), reloaded.StartTime);
  }

  [Fact]
  public async Task Update_note_returns_ok_and_persists_new_note()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var apptId = SeedAppointment(factory.Services, seed, TestDates.InDays(17), new TimeOnly(11, 0));

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    const string newNote = "Klient prosi o nożyczki, nie maszynkę";
    var response = await ownerClient.PatchAsync(
      $"/api/Appointments/{apptId}/note",
      JsonContent.Create(newNote),
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var reloaded = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);

    Assert.Equal(newNote, reloaded.AppointmentNotes);
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices,
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = new Appointment(
      seed.TenantId, seed.EmployeeId, seed.ServiceId, seed.CustomerId,
      date, startTime, startTime.AddMinutes(30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }
}
