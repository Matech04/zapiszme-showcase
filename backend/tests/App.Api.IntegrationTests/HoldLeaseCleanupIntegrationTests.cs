using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// HOLD-005, HOLD-006 — sprawdza, że logika cleanup faktycznie usuwa właściwe wizyty z bazy
/// i pomija wizyty bez dzierżawy (staff-created).
///
/// Test uruchamia <see cref="AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease"/>
/// nad zaseedowanymi appointmentami i wykonuje usunięcie w DB w pętli przypominającej
/// background job — to potwierdza zachowanie end-to-end bez konieczności hostowania zadania.
/// </summary>
public sealed class HoldLeaseCleanupIntegrationTests
{
  [Fact]
  public async Task Cleanup_removes_pending_with_expired_lease_and_preserves_staff_pending_without_lease()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);

    // Wizyty:
    // 1. Pending + wygasła dzierżawa → MUSI być usunięta
    // 2. AwaitingOtp + wygasła dzierżawa → MUSI być usunięta
    // 3. Pending + aktywna dzierżawa → MA pozostać
    // 4. Pending bez dzierżawy (staff) → MUSI pozostać (KRYTYCZNE)
    // 5. Booked + wygasła dzierżawa → MA pozostać (status terminalny dla cleanup)

    var ct = TestContext.Current.CancellationToken;

    Guid id1, id2, id3, id4, id5;

    using (var seedScope = factory.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

      var pendingExpired = MakeAppt(seed, futureDate, new TimeOnly(8, 0),
        AppointmentStatus.Pending,
        lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5)));
      var awaitingExpired = MakeAppt(seed, futureDate, new TimeOnly(9, 0),
        AppointmentStatus.AwaitingOtp,
        lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1)));
      var pendingActive = MakeAppt(seed, futureDate, new TimeOnly(10, 0),
        AppointmentStatus.Pending,
        lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(10)));
      var staffPendingNoLease = MakeAppt(seed, futureDate, new TimeOnly(11, 0),
        AppointmentStatus.Pending,
        lease: null);
      var bookedExpired = MakeAppt(seed, futureDate, new TimeOnly(12, 0),
        AppointmentStatus.Booked,
        lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10)));

      db.Appointments.AddRange(pendingExpired, awaitingExpired, pendingActive, staffPendingNoLease, bookedExpired);
      await db.SaveChangesAsync(ct);

      id1 = pendingExpired.Id;
      id2 = awaitingExpired.Id;
      id3 = pendingActive.Id;
      id4 = staffPendingNoLease.Id;
      id5 = bookedExpired.Id;
    }

    // Run cleanup cycle: query candidates, apply rule, remove
    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var now = DateTime.UtcNow;
      var candidates = await db.Appointments.IgnoreQueryFilters().AsTracking().ToListAsync(ct);
      foreach (var appt in candidates)
      {
        if (AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, now))
        {
          db.Appointments.Remove(appt);
        }
      }
      await db.SaveChangesAsync(ct);
    }

    // Verify final DB state
    using var verifyScope = factory.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await verifyDb.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id1, ct),
      "Pending z wygasłą dzierżawą powinien być usunięty");
    Assert.False(await verifyDb.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id2, ct),
      "AwaitingOtp z wygasłą dzierżawą powinien być usunięty");
    Assert.True(await verifyDb.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id3, ct),
      "Pending z aktywną dzierżawą musi pozostać");
    Assert.True(await verifyDb.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id4, ct),
      "Staff-created Pending (bez dzierżawy) MUSI pozostać — krytyczna regresja");
    Assert.True(await verifyDb.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id5, ct),
      "Booked z wygasłą dzierżawą musi pozostać (nie jest 'held')");
  }

  // HOLD-003 Negative: RequestOtp z wygasłą dzierżawą → 403 AppointmentOtpInvalidLease
  [Fact]
  public async Task Request_otp_with_expired_lease_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var expiredToken = Guid.NewGuid();
    Guid appointmentId;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var appt = new Appointment(
        seed.TenantId, seed.EmployeeId, seed.ServiceId, null,
        TestDates.InDays(40), new TimeOnly(10, 0), new TimeOnly(10, 30),
        AppointmentStatus.AwaitingOtp, new Money(80m, "PLN"), string.Empty,
        new HoldLease(expiredToken, DateTime.UtcNow.AddHours(-1)));
      db.Appointments.Add(appt);
      await db.SaveChangesAsync(ct);
      appointmentId = appt.Id;
    }

    var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync(
      $"/api/booking/{seed.TenantSlug}/public-appointment/{appointmentId}/request-otp",
      new { token = expiredToken, email = "guest@e.local", phoneNumber = (string?)null },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken: ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.AppointmentOtpInvalidLease, problem.ErrorCode);
  }

  private sealed record ProblemDetailsResponse(string ErrorCode);

  private static Appointment MakeAppt(
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime,
    AppointmentStatus status,
    HoldLease? lease)
  {
    return new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      seed.CustomerId,
      date,
      startTime,
      startTime.AddMinutes(30),
      status,
      new Money(80m, "PLN"),
      string.Empty,
      lease);
  }
}
