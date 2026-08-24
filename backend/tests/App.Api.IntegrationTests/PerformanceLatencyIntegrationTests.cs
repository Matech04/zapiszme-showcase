using System.Diagnostics;
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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Api.IntegrationTests;

/// <summary>
/// RTM Performance — PERF-001..010 latency budgets.
///
/// Pomiar: 1 warmup + 5 measured iteracji (read-only); pojedyncza iteracja dla state-changing
/// (hold/OTP) ze względu na 60s cooldown i unikalność stanu.
/// Asercja: MEDIANA <= budget (toleruje GC/JIT outliers). Outputy mierzone w testach
/// to TestServer (bez network stack), więc budżety są luźniejsze niż real-network targets.
///
/// Wszystkie testy podnoszą Global rate-limit (Testing default = 3), żeby nie móc
/// fałszywie 429-ować podczas pomiarów.
/// </summary>
public sealed class PerformanceLatencyIntegrationTests
{
  private readonly ITestOutputHelper _output;

  public PerformanceLatencyIntegrationTests(ITestOutputHelper output)
  {
    _output = output;
  }

  // ── PERF-001 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_public_salon_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = $"/api/booking/{seed.Slug}/public-salon";

    // Warmup (cold path: JIT + EF compile + DI scope)
    var cold = await MeasureAsync(() => client.GetAsync(url, ct));
    _output.WriteLine($"PERF-001 cold: {cold.LatencyMs}ms status={cold.Status}");
    Assert.Equal(HttpStatusCode.OK, cold.Status);

    // 5 mierzonych
    var measurements = await MeasureManyAsync(5, () => client.GetAsync(url, ct));
    LogResults("PERF-001 public-salon", measurements);

    // TestServer budget: 200ms median (prod target po cold-warmup będzie niższy)
    Assert.True(measurements.Median <= 200,
      $"PERF-001 median {measurements.Median}ms > 200ms budget");
  }

  // ── PERF-002 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_booking_services_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = $"/api/booking/{seed.Slug}/services";

    await MeasureAsync(() => client.GetAsync(url, ct));
    var measurements = await MeasureManyAsync(5, () => client.GetAsync(url, ct));
    LogResults("PERF-002 services", measurements);

    Assert.True(measurements.Median <= 150,
      $"PERF-002 median {measurements.Median}ms > 150ms budget");
  }

  // ── PERF-003 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_booking_employees_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = $"/api/booking/{seed.Slug}/employees";

    await MeasureAsync(() => client.GetAsync(url, ct));
    var measurements = await MeasureManyAsync(5, () => client.GetAsync(url, ct));
    LogResults("PERF-003 employees", measurements);

    Assert.True(measurements.Median <= 150,
      $"PERF-003 median {measurements.Median}ms > 150ms budget");
  }

  // ── PERF-004 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_available_slots_for_single_day_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = $"/api/booking/{seed.Slug}/appointments/available-slots"
           + $"?date={TestDates.IsoInDays(15)}&employeeId={seed.EmployeeId}&serviceIds={seed.ServiceId}";

    await MeasureAsync(() => client.GetAsync(url, ct));
    var measurements = await MeasureManyAsync(5, () => client.GetAsync(url, ct));
    LogResults("PERF-004 available-slots", measurements);

    // Cięższe (ResolveScheduleDay + GetWorkingRanges + appointment overlap)
    Assert.True(measurements.Median <= 250,
      $"PERF-004 median {measurements.Median}ms > 250ms budget");
  }

  // ── PERF-005 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_public_salon_unknown_slug_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = "/api/booking/this-slug-does-not-exist-anywhere/public-salon";

    // Warmup
    var warm = await MeasureAsync(() => client.GetAsync(url, ct));
    _output.WriteLine($"PERF-005 cold: {warm.LatencyMs}ms status={warm.Status}");

    var measurements = await MeasureManyAsync(5, () => client.GetAsync(url, ct));
    LogResults("PERF-005 unknown-slug 404", measurements);

    foreach (var s in measurements.Statuses)
    {
      Assert.Equal(HttpStatusCode.NotFound, s);
    }
    // Fast NotFound path — middleware lookup miss
    Assert.True(measurements.Median <= 100,
      $"PERF-005 median {measurements.Median}ms > 100ms budget");
  }

  // ── PERF-006 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Post_public_hold_lease_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var url = $"/api/booking/{seed.Slug}/public-appointment/hold";

    // Warmup z innym slot-em
    var warm = await MeasureAsync(() => client.PostAsJsonAsync(url, new
    {
      serviceIds = new[] { seed.ServiceId },
      employeeId = seed.EmployeeId,
      date = TestDates.IsoInDays(14),
      startTime = "09:00:00",
    }, ct));
    _output.WriteLine($"PERF-006 cold: {warm.LatencyMs}ms status={warm.Status}");

    // Mierzony — pojedyncza iteracja z unikalnym slotem (concurrent hold-lease tested w PERF-017)
    var measurements = new LatencyReport(new List<long>());
    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 3; i++)
    {
      var startMinutes = 10 + i * 2;
      var r = await MeasureAsync(() => client.PostAsJsonAsync(url, new
      {
        serviceIds = new[] { seed.ServiceId },
        employeeId = seed.EmployeeId,
        date = TestDates.IsoInDays(14),
        startTime = $"{startMinutes:00}:00:00",
      }, ct));
      measurements.Latencies.Add(r.LatencyMs);
      statuses.Add(r.Status);
    }
    measurements.Statuses.AddRange(statuses);
    LogResults("PERF-006 hold-lease", measurements);

    Assert.True(measurements.Median <= 250,
      $"PERF-006 median {measurements.Median}ms > 250ms budget");
  }

  // ── PERF-007 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Post_request_otp_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var appointmentId = SeedPendingAppointment(factory.Services, seed, out var leaseToken);

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Warmup tego samego handlera na OSOBNYM appointmencie — cooldown w
    // BookingOtpProtectionService jest per-appointmentId, więc drugi appointment ma czysty stan.
    // GET-warmup nie wystarczył: request-otp ma własny cold-path (Identity, OTP gen, email queue,
    // EF Tenants query). Pierwsza inwokacja ~2.5s, druga w okolicach budżetu.
    var warmupId = SeedPendingAppointment(factory.Services, seed, out var warmupToken);
    var warmupUrl = $"/api/booking/{seed.Slug}/public-appointment/{warmupId}/request-otp";
    var warmupResp = await client.PostAsJsonAsync(warmupUrl, new
    {
      token = warmupToken,
      email = "perf-otp-warmup@perf.local",
      phoneNumber = (string?)null,
    }, ct);
    _output.WriteLine($"PERF-007 warmup status={(int)warmupResp.StatusCode}");

    var url = $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp";
    var measure = await MeasureAsync(() => client.PostAsJsonAsync(url, new
    {
      token = leaseToken,
      email = "perf-otp@perf.local",
      phoneNumber = (string?)null,
    }, ct));

    _output.WriteLine($"PERF-007 request-otp: {measure.LatencyMs}ms status={measure.Status}");
    // Budget 3000ms — po warmupie handlera (osobny appointment) typowo ~2s na devboxie
    // z parallel runnerem. In-isolation byłoby ~120ms; budżet ma chronić przed REGRESJĄ
    // (np. dodanie blokującej operacji w handlerze), a nie przed szumem host'a.
    Assert.True(measure.LatencyMs <= 3000,
      $"PERF-007 latency {measure.LatencyMs}ms > 3000ms budget");
  }

  // ── PERF-008 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Post_verify_otp_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var appointmentId = SeedPendingAppointment(factory.Services, seed, out var leaseToken);

    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Warmup pełnego flow (request-otp + verify-otp) na OSOBNYM appointmencie — cooldown jest
    // per-appointmentId, więc to zwarmuje oba handlery bez konsumpcji budżetu mierzonej iteracji.
    var warmupId = SeedPendingAppointment(factory.Services, seed, out var warmupToken);
    var warmupReqUrl = $"/api/booking/{seed.Slug}/public-appointment/{warmupId}/request-otp";
    var warmupVerifyUrl = $"/api/booking/{seed.Slug}/public-appointment/{warmupId}/verify-otp";
    var warmupReq = await client.PostAsJsonAsync(warmupReqUrl, new
    {
      token = warmupToken,
      email = "perf-verify-warmup@perf.local",
      phoneNumber = (string?)null,
    }, ct);
    _output.WriteLine($"PERF-008 warmup request status={(int)warmupReq.StatusCode}");
    var warmupOtp = mailbox.LastCode ?? throw new InvalidOperationException("PERF-008 warmup OTP not captured");
    var warmupVerify = await client.PostAsJsonAsync(warmupVerifyUrl, new { token = warmupToken, otp = warmupOtp }, ct);
    _output.WriteLine($"PERF-008 warmup verify status={(int)warmupVerify.StatusCode}");

    var requestUrl = $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp";
    var verifyUrl = $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/verify-otp";

    var reqResp = await client.PostAsJsonAsync(requestUrl, new
    {
      token = leaseToken,
      email = "perf-verify@perf.local",
      phoneNumber = (string?)null,
    }, ct);
    Assert.True(reqResp.IsSuccessStatusCode,
      $"PERF-008 request-otp setup failed ({(int)reqResp.StatusCode})");

    var otp = mailbox.LastCode ?? throw new InvalidOperationException("OTP not captured");

    var measure = await MeasureAsync(() => client.PostAsJsonAsync(verifyUrl, new
    {
      token = leaseToken,
      otp,
    }, ct));

    _output.WriteLine($"PERF-008 verify-otp: {measure.LatencyMs}ms status={measure.Status}");
    // Patrz PERF-007 — 3000ms budżet po warmupie pełnego flow (request+verify) na osobnym
    // appointmencie. Chroni przed regresją handlera, nie przed szumem host'a.
    Assert.True(measure.LatencyMs <= 3000,
      $"PERF-008 latency {measure.LatencyMs}ms > 3000ms budget");
  }

  // ── PERF-009 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Post_auth_login_median_latency_under_budget()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var body = new
    {
      email = "owner@rest-seed.local",
      password = "Password123!",
      turnstileToken = (string?)null,
    };

    // Warmup (Identity init, password hasher cache)
    var cold = await MeasureAsync(() => client.PostAsJsonAsync("/api/auth/login", body, ct));
    _output.WriteLine($"PERF-009 cold: {cold.LatencyMs}ms status={cold.Status}");

    var measurements = await MeasureManyAsync(5, () => client.PostAsJsonAsync("/api/auth/login", body, ct));
    LogResults("PERF-009 login", measurements);

    // PBKDF2 100k iteracji dominuje. TestServer budget: 500ms median.
    Assert.True(measurements.Median <= 600,
      $"PERF-009 median {measurements.Median}ms > 600ms budget");
  }

  // ── PERF-010 ────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Tenant_resolution_middleware_overhead_under_budget_vs_health_endpoint()
  {
    using var factory = BuildHighLimitFactory();
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Warmup obu ścieżek
    await MeasureAsync(() => client.GetAsync("/health/live", ct));
    await MeasureAsync(() => client.GetAsync($"/api/booking/{seed.Slug}/public-salon", ct));

    // Health = brak tenant-resolution
    var health = await MeasureManyAsync(5, () => client.GetAsync("/health/live", ct));
    // Public-salon = z tenant-resolution
    var salon = await MeasureManyAsync(5, () => client.GetAsync($"/api/booking/{seed.Slug}/public-salon", ct));

    LogResults("PERF-010 /health/live", health);
    LogResults("PERF-010 /public-salon (z tenant resolve)", salon);

    var deltaMs = salon.Median - health.Median;
    _output.WriteLine($"PERF-010 tenant-resolve overhead approx: {deltaMs}ms (salon median {salon.Median} - health median {health.Median})");

    // Delta to mix tenant-resolve + handler-work — generous budget aby nie było flaky:
    // <= 200ms (tj. tenant lookup nie powinien dominować koło handlera read-only)
    Assert.True(deltaMs <= 200,
      $"PERF-010 delta {deltaMs}ms > 200ms — middleware tenant-resolve może dominować");
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────

  private static WebApplicationFactory<Program> BuildHighLimitFactory()
  {
    var baseFactory = new BookingApiApplicationFactory();
    return baseFactory.WithWebHostBuilder(builder =>
    {
      // Testing env defaults: Global=3, PublicBookingWrite=2. Podnosimy aby nie 429-ować podczas pomiarów.
      builder.UseSetting("RateLimiting:Global:PermitLimit", "10000");
      builder.UseSetting("RateLimiting:Global:WindowSeconds", "60");
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "10000");
      builder.UseSetting("RateLimiting:PublicBookingWrite:WindowSeconds", "60");
      builder.UseSetting("RateLimiting:Auth:PermitLimit", "10000");
    });
  }

  private sealed record SingleMeasurement(long LatencyMs, HttpStatusCode Status);

  private sealed record LatencyReport(List<long> Latencies)
  {
    public List<HttpStatusCode> Statuses { get; } = new();
    public long Min => Latencies.Count == 0 ? 0 : Latencies.Min();
    public long Max => Latencies.Count == 0 ? 0 : Latencies.Max();
    public long Median
    {
      get
      {
        if (Latencies.Count == 0) return 0;
        var sorted = Latencies.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
      }
    }
  }

  private static async Task<SingleMeasurement> MeasureAsync(Func<Task<HttpResponseMessage>> action)
  {
    var sw = Stopwatch.StartNew();
    var resp = await action();
    sw.Stop();
    return new SingleMeasurement(sw.ElapsedMilliseconds, resp.StatusCode);
  }

  private static async Task<LatencyReport> MeasureManyAsync(int iterations, Func<Task<HttpResponseMessage>> action)
  {
    var report = new LatencyReport(new List<long>());
    for (var i = 0; i < iterations; i++)
    {
      var m = await MeasureAsync(action);
      report.Latencies.Add(m.LatencyMs);
      report.Statuses.Add(m.Status);
    }
    return report;
  }

  private void LogResults(string label, LatencyReport r)
  {
    _output.WriteLine($"{label}: min={r.Min}ms median={r.Median}ms max={r.Max}ms (N={r.Latencies.Count})");
  }

  private sealed record PerfSeed(string Slug, Guid TenantId, Guid EmployeeId, Guid ServiceId);

  private static PerfSeed SeedPerfTenant(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var slug = "perf-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var tenant = new Tenant("Perf Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Perf", "Worker", "perf@worker.local");

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(18, 0)),
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

    return new PerfSeed(slug, tenant.Id, employee.Id, service.Id);
  }

  private static int _pendingSlotCounter;

  private static Guid SeedPendingAppointment(IServiceProvider rootServices, PerfSeed seed, out Guid leaseToken)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));

    // KAŻDE wywołanie dostaje własny dzień. PERF-007 woła ten seed dwa razy (pomiar + rozgrzewka
    // handlera), a `IX_Appointments_EmployeeId_Date_StartTime` jest UNIKATOWY dla wizyt nieanulowanych.
    // InMemory nie egzekwuje indeksów, więc duplikat przechodził tam bez słowa; Postgres odrzuca.
    // Pracownik ma grafik 8–18 przez wszystkie dni tygodnia, więc przesunięcie dnia jest bezpieczne.
    var slot = Interlocked.Increment(ref _pendingSlotCounter);

    var appointment = new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      customerId: null,
      TestDates.InDays(30 + slot),
      new TimeOnly(9, 0),
      new TimeOnly(10, 0),
      AppointmentStatus.Pending,
      new Money(50m, "PLN"),
      string.Empty,
      lease);

    db.Appointments.Add(appointment);
    db.SaveChanges();

    return appointment.Id;
  }
}
