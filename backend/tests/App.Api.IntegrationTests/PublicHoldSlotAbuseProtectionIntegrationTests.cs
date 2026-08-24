using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
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
/// Anti-slot-hoarding: użytkownik tworzy nowy /hold mając już aktywny (np. po F5 i wyborze
/// innego slotu) — poprzedni jest automatycznie anulowany (cookie anonymous-session śledzi
/// te same usera). Plus warstwa 3: initial TTL holdu = 180s (czas na wpisanie danych), odświeżany do 3 min na RequestOtp.
/// </summary>
public sealed class PublicHoldSlotAbuseProtectionIntegrationTests
{
  [Fact]
  public async Task Second_hold_from_same_anon_session_removes_previous_pending_appointment()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);

    // TestServer client z HandleCookies=true automatycznie zachowuje cookie z odpowiedzi
    // i wysyła z każdym kolejnym requestem (= przeglądarka).
    var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
    var ct = TestContext.Current.CancellationToken;

    var first = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(30), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    var firstHold = await first.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(firstHold);

    // Drugi hold z tego samego klienta (więc tym samym cookie) — poprzedni (niepotwierdzony) musi zniknąć.
    var second = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(30), startTime = "11:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Stary slot zwolniony — niepotwierdzony hold jest USUWANY (nie zostawiany jako Canceled tombstone).
    var firstStillExists = await db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == firstHold!.AppointmentId, ct);
    Assert.False(firstStillExists, "Poprzedni niepotwierdzony hold powinien zostać usunięty z bazy.");
  }

  [Fact]
  public async Task Holds_from_different_anon_sessions_do_not_cancel_each_other()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);

    // Każdy CreateClient(HandleCookies=true) zwraca osobny cookie-store = dwie różne sesje.
    var clientA = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
    var clientB = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
    var ct = TestContext.Current.CancellationToken;

    var aResp = await clientA.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(31), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, aResp.StatusCode);
    var aHold = await aResp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);

    var bResp = await clientB.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(31), startTime = "11:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, bResp.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var aAppointment = await db.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == aHold!.AppointmentId, ct);
    // Klient A NIE może zostać anulowany przez klienta B — różne sesje.
    Assert.NotEqual(AppointmentStatus.Canceled, aAppointment.Status);
  }

  [Fact]
  public async Task Initial_hold_lease_ttl_is_180_seconds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var nowUtcBefore = DateTime.UtcNow;
    var resp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(32), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var hold = await resp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(hold);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appointment = await db.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == hold!.AppointmentId, ct);
    Assert.NotNull(appointment.Lease);
    var ttlSeconds = (appointment.Lease!.ExpiryTimeUtc - nowUtcBefore).TotalSeconds;
    // Initial = 180s ±5s tolerancji (czas dla klienta na wpisanie danych przed OTP).
    Assert.InRange(ttlSeconds, 175, 185);
  }

  // Squatter który nigdy nie zacznie OTP — lease wygasa po 180s. Po OTP request lease
  // odświeża się na kolejne 3 min, żeby legit user zdążył odebrać mail/SMS i wpisać kod.
  [Fact]
  public async Task RequestOtp_extends_lease_to_three_minutes()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
    var ct = TestContext.Current.CancellationToken;

    var holdResp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(34), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, holdResp.StatusCode);
    var hold = await holdResp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(hold);

    var beforeOtp = DateTime.UtcNow;
    var otpResp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/{hold!.AppointmentId}/request-otp",
      new { token = hold.Lease.ReservationToken, email = "guest@e.local" },
      ct);
    Assert.Equal(HttpStatusCode.OK, otpResp.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appointment = await db.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == hold.AppointmentId, ct);
    var newTtlSeconds = (appointment.Lease!.ExpiryTimeUtc - beforeOtp).TotalSeconds;
    // 3 min (180s) ±5s na tolerancję.
    Assert.InRange(newTtlSeconds, 175, 185);
    // Token NIE zmienia się przy wydłużeniu — frontend nadal może dokończyć flow OTP.
    Assert.Equal(hold.Lease.ReservationToken, appointment.Lease.ReservationToken);
  }

  [Fact]
  public async Task Anon_session_cookie_is_httponly_lax_essential()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var resp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(33), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
    var anonCookie = cookies!.FirstOrDefault(c => c.Contains("booking_saas_anon_session", StringComparison.Ordinal));
    Assert.NotNull(anonCookie);
    Assert.Contains("httponly", anonCookie!, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("max-age=", anonCookie, StringComparison.OrdinalIgnoreCase);
    // Testing env jedzie po HTTP, więc SameSite schodzi do Lax (Secure cookie byłoby
    // odrzucone przez przeglądarkę). W prawdziwym prod CrossOriginCookiePolicy daje
    // SameSite=None+Secure, żeby cookie leciało w cross-site fetch booking-web → api —
    // gałąź prod pokrywa CrossOriginCookiePolicyTests (testy integracyjne biegną w "Testing").
    Assert.Contains("samesite=lax", anonCookie, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("samesite=none", anonCookie, StringComparison.OrdinalIgnoreCase);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────

  private sealed record HoldResponse(Guid AppointmentId, LeaseDto Lease);
  private sealed record LeaseDto(Guid ReservationToken, DateTime ExpiryTimeUtc);

  private sealed record SeedResult(string Slug, Guid TenantId, Guid EmployeeId, Guid ServiceId);

  private static SeedResult SeedTenant(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var slug = "abuse-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var tenant = new Tenant("Slot Abuse Salon", slug);
    // Test wysyła OTP na e-mail — pinujemy kanał (domyślny to teraz Phone/SMS).
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "A", "T", "a@t.local");

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges);
    employee.SetWeeklySchedule(weekly);

    var service = new Service(tenant.Id, category.Id, vat.Id, "S", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, 30, new Money(50m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.SaveChanges();

    return new SeedResult(slug, tenant.Id, employee.Id, service.Id);
  }
}
