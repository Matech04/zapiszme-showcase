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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Preflight hardening:
///   [M3] Twardy cap liczby JEDNOCZEŚNIE aktywnych holdów per IP — rotacja AnonSessionId (osobne
///        cookie-store z jednego loopback-IP) omija auto-anulowanie per-sesji → slot hoarding.
///        Po progu MaxConcurrentHoldsPerIp /hold zwraca 429. Legalny pojedynczy user (jedno cookie,
///        nowy /hold anuluje poprzedni) nigdy nie dobija.
///   [M4] Gate subskrypcji w write-flow: salon z EffectiveStatus poza {Trial, Active} (Canceled /
///        PastDue) odrzuca /hold ORAZ request-otp PRZED wysyłką OTP (brak fałszywej rezerwacji /
///        drenażu SMS).
/// </summary>
public sealed class BookingAbuseHardCapsIntegrationTests
{
  // ── [M3] ───────────────────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Rotating_anon_session_from_same_ip_is_capped_at_max_concurrent_holds()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
      builder.UseSetting("Booking:OtpProtection:MaxConcurrentHoldsPerIp", "5");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Każdy CreateClient z osobnym cookie-store = osobny AnonSessionId, ale ten sam loopback-IP.
    // Bez cap: każdy hold to świeża sesja → nic nie anuluje → nieograniczone squatting.
    var accepted = 0;
    var rejected = 0;
    for (var i = 0; i < 8; i++)
    {
      var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
      client.DefaultRequestHeaders.Add(TestClientIpHeader.Name, "203.0.113.10");
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/hold",
        new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(40), startTime = $"{8 + i:00}:00:00" },
        ct);
      if (resp.StatusCode == HttpStatusCode.OK) accepted++;
      else if (resp.StatusCode == HttpStatusCode.TooManyRequests) rejected++;
    }

    Assert.Equal(5, accepted);
    Assert.Equal(3, rejected);
  }

  [Fact]
  public async Task Legitimate_single_user_cancelling_previous_hold_is_not_capped()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
      builder.UseSetting("Booking:OtpProtection:MaxConcurrentHoldsPerIp", "5");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // JEDEN client (jedno cookie = jedna sesja). Każdy nowy /hold anuluje poprzedni (release per-IP),
    // więc aktywny jest zawsze 1 hold — 10 kolejnych slotów nie dobija do progu 5.
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    client.DefaultRequestHeaders.Add(TestClientIpHeader.Name, "203.0.113.11");
    for (var i = 0; i < 10; i++)
    {
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/hold",
        new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(41), startTime = $"{8 + i:00}:00:00" },
        ct);
      Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
  }

  [Fact]
  public async Task Confirmed_bookings_from_same_ip_release_the_concurrent_holds_cap()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
      builder.UseSetting("Booking:OtpProtection:MaxConcurrentHoldsPerIp", "5");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var ct = TestContext.Current.CancellationToken;

    // CGNAT: sześciu RÓŻNYCH użytkowników (osobne cookie = osobne AnonSessionId) za jednym publicznym
    // IP operatora. Każdy domyka rezerwację (hold → request-otp → verify-otp), więc jego hold przestaje
    // istnieć. Licznik [M3] ma mierzyć holdy AKTYWNE — potwierdzona wizyta nie może zajmować miejsca,
    // inaczej szósty legalny klient dostaje 429 mimo że żaden hold nie żyje.
    for (var i = 0; i < 6; i++)
    {
      var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
      client.DefaultRequestHeaders.Add(TestClientIpHeader.Name, "203.0.113.30");

      var holdResp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/hold",
        new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(45), startTime = $"{8 + i:00}:00:00" },
        ct);
      Assert.Equal(HttpStatusCode.OK, holdResp.StatusCode);
      var hold = (await holdResp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct))!;

      var otpResp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/{hold.AppointmentId}/request-otp",
        new { token = hold.Lease.ReservationToken, email = $"guest{i}@e.local", phoneNumber = (string?)null },
        ct);
      Assert.Equal(HttpStatusCode.OK, otpResp.StatusCode);

      var verifyResp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/{hold.AppointmentId}/verify-otp",
        new { token = hold.Lease.ReservationToken, otp = mailbox.LastCode, firstName = "Jan", lastName = "Kowalski" },
        ct);
      Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
    }

    // Wszystkie sześć doszły do skutku — żaden legalny klient nie oberwał capem.
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Assert.Equal(6, await db.Appointments.IgnoreQueryFilters()
      .CountAsync(a => a.TenantId == seed.TenantId && a.Status == AppointmentStatus.Booked, ct));
  }

  [Fact]
  public async Task Second_hold_in_same_session_hard_deletes_previous_hold_not_leaves_canceled()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Jedna sesja (jedno cookie). Drugi /hold tej samej sesji uruchamia anti-abuse.
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    client.DefaultRequestHeaders.Add(TestClientIpHeader.Name, "203.0.113.20");

    async Task<HoldResponse> Hold(string startTime)
    {
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/hold",
        new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(42), startTime },
        ct);
      Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
      return (await resp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct))!;
    }

    var first = await Hold("09:00:00");
    var second = await Hold("10:00:00");

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Poprzedni niepotwierdzony hold MUSI zostać twardo usunięty — nie zostawiony jako Canceled.
    Assert.False(
      await db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == first.AppointmentId, ct),
      "Poprzedni hold AwaitingOtp powinien zostać usunięty z bazy, nie pozostawiony jako Canceled.");

    // Nowy hold istnieje i jest aktywny (AwaitingOtp).
    var current = await db.Appointments.IgnoreQueryFilters()
      .SingleAsync(a => a.Id == second.AppointmentId, ct);
    Assert.Equal(AppointmentStatus.AwaitingOtp, current.Status);

    // Żadnych tombstone'ów Canceled po anti-abuse.
    Assert.Equal(0, await db.Appointments.IgnoreQueryFilters()
      .CountAsync(a => a.TenantId == seed.TenantId && a.Status == AppointmentStatus.Canceled, ct));
  }

  // ── [M1] ───────────────────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Rotating_phone_number_from_same_ip_is_capped_on_sent_otp_sms()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
      // Niski cap, żeby test był szybki: 4 SMS/IP/h.
      builder.UseSetting("Booking:OtpProtection:MaxOtpSmsPerIpPerHour", "4");
    });
    _ = factory.Services;
    // Kanał SMS — pinujemy na Phone (M1 dotyczy OTP-SMS).
    var seed = SeedTenant(factory.Services, verificationChannel: CustomerVerificationChannel.Phone);
    var phoneMailbox = factory.Services.GetRequiredService<TestPhoneOtpMailbox>();
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestClientIpHeader.Name, "203.0.113.12");
    var ct = TestContext.Current.CancellationToken;

    // Symulacja rotacji numeru z jednego IP: każdy request-otp na INNY (polski) numer i osobny hold.
    // Per-numer capy się resetują (nowy numer), ale per-IP/h licznik wysłanych SMS rośnie.
    var accepted = 0;
    var rejected = 0;
    for (var i = 0; i < 8; i++)
    {
      var appointmentId = SeedHoldAppointment(factory.Services, seed, out var leaseToken);
      var phone = $"+4850100{i:D4}";
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp",
        new { token = leaseToken, email = (string?)null, phoneNumber = phone },
        ct);
      if (resp.StatusCode == HttpStatusCode.OK) accepted++;
      else if (resp.StatusCode == HttpStatusCode.TooManyRequests) rejected++;
    }

    Assert.Equal(4, accepted);
    Assert.Equal(4, rejected);
    // Mailbox potwierdza: dokładnie 4 SMS wyszło mimo 8 prób z różnych numerów (jeden IP).
    Assert.Equal(4, phoneMailbox.Calls.Count);
  }

  // ── [M4] ───────────────────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Hold_is_rejected_for_salon_with_canceled_subscription()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    var seed = SeedTenant(factory.Services, cancelSubscription: true);
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    var ct = TestContext.Current.CancellationToken;

    var resp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(42), startTime = "10:00:00" },
      ct);

    // BookingUnavailableException → 400 (DomainException). Brak utworzonego holdu.
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var holdCount = await db.Appointments.IgnoreQueryFilters().CountAsync(a => a.TenantId == seed.TenantId, ct);
    Assert.Equal(0, holdCount);
  }

  [Fact]
  public async Task RequestOtp_is_rejected_for_salon_with_canceled_subscription_and_sends_no_otp()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    // Seedujemy hold BEZPOŚREDNIO w bazie (omijając /hold), a subskrypcję ustawiamy na Canceled —
    // symuluje spreparowanego klienta wołającego request-otp na nieaktywnym salonie z gotowym leasem.
    var seed = SeedTenant(factory.Services, cancelSubscription: true);
    var appointmentId = SeedHoldAppointment(factory.Services, seed, out var leaseToken);
    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var resp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp",
      new { token = leaseToken, email = "victim@e.local", phoneNumber = (string?)null },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    // Krytyczne: ŻADEN OTP nie został wysłany (brak drenażu).
    Assert.Empty(mailbox.AllSent);
  }

  [Fact]
  public async Task Hold_and_request_otp_succeed_for_trial_salon()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });
    _ = factory.Services;
    // Domyślny seed = Trial (StartTrial) → booking dostępny.
    var seed = SeedTenant(factory.Services);
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    var ct = TestContext.Current.CancellationToken;

    var holdResp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/hold",
      new { serviceIds = new[] { seed.ServiceId }, employeeId = seed.EmployeeId, date = TestDates.IsoInDays(43), startTime = "10:00:00" },
      ct);
    Assert.Equal(HttpStatusCode.OK, holdResp.StatusCode);
    var hold = await holdResp.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(hold);

    var otpResp = await client.PostAsJsonAsync(
      $"/api/booking/{seed.Slug}/public-appointment/{hold!.AppointmentId}/request-otp",
      new { token = hold.Lease.ReservationToken, email = "guest@e.local" },
      ct);
    Assert.Equal(HttpStatusCode.OK, otpResp.StatusCode);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────

  private sealed record HoldResponse(Guid AppointmentId, LeaseDto Lease);
  private sealed record LeaseDto(Guid ReservationToken, DateTime ExpiryTimeUtc);

  private sealed record SeedResult(string Slug, Guid TenantId, Guid EmployeeId, Guid ServiceId);

  private static SeedResult SeedTenant(
    IServiceProvider rootServices,
    bool cancelSubscription = false,
    CustomerVerificationChannel verificationChannel = CustomerVerificationChannel.Email)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var slug = "caps-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var tenant = new Tenant("Hard Caps Salon", slug);
    // Pinujemy kanał weryfikacji (domyślny model = Phone/SMS; większość testów chce Email).
    tenant.Update(tenant.Name, tenant.Slug, verificationChannel);
    if (cancelSubscription)
    {
      var sub = Subscription.StartTrial();
      sub.Cancel();
      tenant.SetSubscription(sub);
    }
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

  private static int _holdSlotCounter;

  private static Guid SeedHoldAppointment(IServiceProvider rootServices, SeedResult seed, out Guid leaseToken)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));
    var appointment = new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      customerId: null,
      // Własny dzień na wywołanie — test woła ten seed w pętli 8×, a indeks
      // IX_Appointments_EmployeeId_Date_StartTime jest unikatowy dla wizyt nieanulowanych.
      // InMemory tego nie egzekwował, Postgres tak.
      TestDates.InDays(44 + Interlocked.Increment(ref _holdSlotCounter)),
      new TimeOnly(10, 0),
      new TimeOnly(10, 30),
      AppointmentStatus.AwaitingOtp,
      new Money(50m, "PLN"),
      string.Empty,
      lease);

    db.Appointments.Add(appointment);
    db.SaveChanges();

    return appointment.Id;
  }
}
