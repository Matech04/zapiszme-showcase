using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class PublicBookingApiTests
{
  private const string Slug = "integration-booking-salon";
  private const string OtherSlug = "integration-booking-salon-other";

  private static void SeedPendingAppointmentWithLease(
      IServiceProvider rootServices,
      string slug,
      out Guid appointmentId,
      out Guid leaseToken,
      out Guid employeeId,
      out Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Integration Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Eva", "Test", "eva@test.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(service.Price.Amount, service.Price.Currency));

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));
    var appointment = new Appointment(
        tenant.Id,
        employee.Id,
        service.Id,
        customerId: null,
        TestDates.InDays(15),
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        AppointmentStatus.Pending,
        new Money(50m, "PLN"),
        string.Empty,
        lease);

    appointmentId = appointment.Id;
    employeeId = employee.Id;
    serviceId = service.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    db.SaveChanges();
  }

  [Fact]
  public async Task Get_public_salon_returns_ok_and_payload()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out _, out _);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync($"/api/booking/{Slug}/public-salon", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var dto = await response.Content.ReadFromJsonAsync<PublicSalonResponse>(jsonOptions, ct);
    Assert.NotNull(dto);
    Assert.Equal(Slug, dto.Slug);
    Assert.Equal("Integration Salon", dto.Name);
    Assert.Equal(1, dto.CustomerVerificationChannel);
  }

  [Fact]
  public async Task Request_otp_then_verify_otp_marks_appointment_booked()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out var appointmentId, out var leaseToken, out _, out _);

    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var requestOtp = await client.PostAsJsonAsync(
        $"/api/booking/{Slug}/public-appointment/{appointmentId}/request-otp",
        new { token = leaseToken, phoneNumber = (string?)null, email = "booker@example.com" },
        ct);

    Assert.Equal(HttpStatusCode.OK, requestOtp.StatusCode);
    Assert.NotNull(mailbox.LastCode);
    Assert.Matches(@"^\d{6}$", mailbox.LastCode);
    Assert.Equal("booker@example.com", mailbox.LastToEmail);

    var verify = await client.PostAsJsonAsync(
        $"/api/booking/{Slug}/public-appointment/{appointmentId}/verify-otp",
        new { token = leaseToken, otp = mailbox.LastCode },
        ct);

    Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var reloaded = await db.Appointments.AsNoTracking().IgnoreQueryFilters()
        .FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentStatus.Booked, reloaded.Status);
  }

  [Fact]
  public async Task Request_otp_with_invalid_lease_returns_403()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out var appointmentId, out _, out _, out _);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
        $"/api/booking/{Slug}/public-appointment/{appointmentId}/request-otp",
        new { token = Guid.NewGuid(), phoneNumber = (string?)null, email = "x@y.z" },
        ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken: ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.AppointmentOtpInvalidLease, problem.ErrorCode);
  }

  [Fact]
  public async Task Request_otp_under_slug_cannot_access_appointment_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out _, out _);
    SeedPendingAppointmentWithLease(factory.Services, OtherSlug, out var otherAppointmentId, out var otherLeaseToken, out _, out _);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/{otherAppointmentId}/request-otp",
      new { token = otherLeaseToken, phoneNumber = (string?)null, email = "x@y.z" },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Verify_otp_under_slug_cannot_access_appointment_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out _, out _);
    SeedPendingAppointmentWithLease(factory.Services, OtherSlug, out var otherAppointmentId, out var otherLeaseToken, out _, out _);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/{otherAppointmentId}/verify-otp",
      new { token = otherLeaseToken, otp = "123456" },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Create_hold_under_slug_cannot_use_employee_or_service_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out _, out _);
    SeedPendingAppointmentWithLease(factory.Services, OtherSlug, out _, out _, out var otherEmployeeId, out var otherServiceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new
      {
        serviceIds = new[] { otherServiceId },
        employeeId = otherEmployeeId,
        date = TestDates.IsoInDays(14),
        startTime = "10:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Available_slots_under_slug_cannot_use_employee_or_service_from_other_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out _, out _);
    SeedPendingAppointmentWithLease(factory.Services, OtherSlug, out _, out _, out var otherEmployeeId, out var otherServiceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Data LICZONA: przy zaszytej „2026-08-04" po tym dniu wracało 400 (termin przeszły) zamiast
    // 404 — test przestawał pilnować izolacji tenantów w publicznym bookingu.
    var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd");
    var response = await client.GetAsync(
      $"/api/booking/{Slug}/appointments/available-slots?date={date}&employeeId={otherEmployeeId}&serviceIds={otherServiceId}",
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Create_hold_same_slot_twice_does_not_allow_two_successes()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var first = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new
      {
        serviceIds = new[] { serviceId },
        employeeId,
        date = TestDates.IsoInDays(16),
        startTime = "10:00:00",
      },
      ct);

    var second = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new
      {
        serviceIds = new[] { serviceId },
        employeeId,
        date = TestDates.IsoInDays(16),
        startTime = "10:00:00",
      },
      ct);

    var successCount = new[] { first, second }.Count(r => r.StatusCode == HttpStatusCode.OK);
    Assert.True(successCount <= 1, $"At most one request should succeed, but got {successCount}.");
  }

  [Fact]
  public async Task Create_hold_parallel_same_slot_does_not_allow_two_successes()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out var employeeId, out var serviceId);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var requestBody = new
    {
      serviceIds = new[] { serviceId },
      employeeId,
      date = TestDates.IsoInDays(17),
      startTime = "11:00:00",
    };

    var firstTask = client.PostAsJsonAsync($"/api/booking/{Slug}/public-appointment/hold", requestBody, ct);
    var secondTask = client.PostAsJsonAsync($"/api/booking/{Slug}/public-appointment/hold", requestBody, ct);
    var responses = await Task.WhenAll(firstTask, secondTask);

    var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
    Assert.True(successCount <= 1, $"At most one request should succeed, but got {successCount}.");
  }

  [Fact]
  public async Task Booking_pause_flags_salon_and_blocks_hold()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, Slug, out _, out _, out var employeeId, out var serviceId);

    // Wstrzymaj rezerwacje na zaseedowanym salonie.
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = db.Tenants.Single(t => t.Slug == Slug);
      tenant.SetBookingPause(true, "Rezerwacje wstrzymane");
      db.SaveChanges();
    }

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // 1) Read-query oznacza salon jako wstrzymany i niedostępny do rezerwacji.
    var salon = await client.GetAsync($"/api/booking/{Slug}/public-salon", ct);
    Assert.Equal(HttpStatusCode.OK, salon.StatusCode);
    var dto = await salon.Content.ReadFromJsonAsync<BookingPauseSalonResponse>(jsonOptions, ct);
    Assert.NotNull(dto);
    Assert.True(dto.IsBookingPaused);
    Assert.False(dto.IsBookingAvailable);
    Assert.Equal("Rezerwacje wstrzymane", dto.BookingPauseMessage);

    // 2) Write-flow (hold) jest zablokowany — spreparowany klient nie obejdzie wstrzymania rezerwacji.
    var hold = await client.PostAsJsonAsync(
      $"/api/booking/{Slug}/public-appointment/hold",
      new { serviceIds = new[] { serviceId }, employeeId, date = TestDates.IsoInDays(19), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.BadRequest, hold.StatusCode);
    var problem = await hold.Content.ReadFromJsonAsync<ProblemDetailsResponse>(jsonOptions, ct);
    Assert.Equal(ErrorCodes.BookingPaused, problem?.ErrorCode);
  }

  private sealed record PublicSalonResponse(string Name, string Slug, int CustomerVerificationChannel);

  private sealed record BookingPauseSalonResponse(
    bool IsBookingAvailable,
    bool IsBookingPaused,
    string? BookingPauseMessage);

  private sealed record ProblemDetailsResponse(string? ErrorCode);
}
