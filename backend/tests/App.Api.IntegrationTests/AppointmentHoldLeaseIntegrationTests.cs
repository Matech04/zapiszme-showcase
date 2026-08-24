using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// APPT-007 — publiczna rezerwacja (hold) tworzy wizytę ze statusem AwaitingOtp i dzierżawą 3 min.
/// (TTL skrócony z 10 → 3 min jako anti-slot-hoarding warstwa 3; patrz PublicHoldSlotAbuseProtectionIntegrationTests.)
/// APPT-007 Negative — weryfikacja OTP z wygasłą dzierżawą zwraca 403.
/// </summary>
public sealed class AppointmentHoldLeaseIntegrationTests
{
  private const string HoldSlug = "appt-hold-lease-tests";

  // IT-APPT: APPT-007 HappyPath — APP-APPT (sekcja: APPT-007)
  // Initial lease TTL 180s — czas dla klienta na wpisanie danych kontaktowych przed OTP.
  // RequestOtp odświeża lease do kolejnych 3 min (OtpLease).
  [Fact]
  public async Task Hold_creates_appointment_with_awaiting_otp_status_and_initial_180s_lease()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var (employeeId, serviceId) = SeedSalonForHold(factory.Services, HoldSlug + "-a");
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var before = DateTime.UtcNow;
    // Data WZGLĘDNA, nie zaszyta: rezerwacja online obowiązuje horyzont (Tenant.BookingHorizonDays,
    // domyślnie 120 dni), więc sztywna data w miarę upływu czasu wchodzi i wychodzi z okna
    // rezerwacji — test przestawał dotyczyć holdu, a zaczynał horyzontu.
    var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    var response = await client.PostAsJsonAsync(
      $"/api/booking/{HoldSlug}-a/public-appointment/hold",
      new
      {
        employeeId,
        serviceIds = new[] { serviceId },
        date = date.ToString("yyyy-MM-dd"),
        startTime = "10:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var hold = await response.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(hold);
    Assert.NotEqual(Guid.Empty, hold.AppointmentId);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(a => a.Id == hold.AppointmentId, ct);

    Assert.NotNull(appt);
    Assert.Equal(AppointmentStatus.AwaitingOtp, appt.Status);
    Assert.NotNull(appt.Lease);
    Assert.Equal(AppointmentSource.Online, appt.Source);

    var minExpected = before.AddSeconds(175);
    var maxExpected = before.AddSeconds(185);
    Assert.True(appt.Lease.ExpiryTimeUtc >= minExpected,
      $"Lease.ExpiryTimeUtc ({appt.Lease.ExpiryTimeUtc:O}) should be at least 175s from now ({minExpected:O})");
    Assert.True(appt.Lease.ExpiryTimeUtc <= maxExpected,
      $"Lease.ExpiryTimeUtc ({appt.Lease.ExpiryTimeUtc:O}) should not exceed 185s from now ({maxExpected:O})");
  }

  // IT-APPT: APPT-007 Negative — APP-APPT (sekcja: APPT-007)
  [Fact]
  public async Task Verify_otp_with_expired_hold_lease_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var slug = HoldSlug + "-b";
    SeedSalonForHold(factory.Services, slug);

    var expiredLeaseToken = Guid.NewGuid();
    var appointmentId = SeedAppointmentWithExpiredLease(factory.Services, slug, expiredLeaseToken);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{slug}/public-appointment/{appointmentId}/verify-otp",
      new { token = expiredLeaseToken, otp = "123456" },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken: ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.AppointmentOtpInvalidLease, problem.ErrorCode);
  }

  private static (Guid employeeId, Guid serviceId) SeedSalonForHold(IServiceProvider rootServices, string slug)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Hold Test Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT 23%", 0.23m);
    var employee = new Employee(tenant.Id, null, "Test", "Worker", "worker@hold.local");

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges);
    employee.SetWeeklySchedule(weekly);

    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(service.Price.Amount, service.Price.Currency));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.SaveChanges();

    return (employee.Id, service.Id);
  }

  private static Guid SeedAppointmentWithExpiredLease(IServiceProvider rootServices, string slug, Guid leaseToken)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Slug == slug);
    var employee = db.Employees.IgnoreQueryFilters().First(e => e.TenantId == tenant.Id);
    var service = db.Services.IgnoreQueryFilters().First(s => s.TenantId == tenant.Id);

    var expiredLease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(-1));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, null,
      TestDates.InDays(60),
      new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.AwaitingOtp,
      new Money(80m, "PLN"),
      string.Empty,
      expiredLease,
      AppointmentSource.Online);

    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }

  private sealed record HoldResponse(Guid AppointmentId);
  private sealed record ProblemDetailsResponse(string ErrorCode);
}
