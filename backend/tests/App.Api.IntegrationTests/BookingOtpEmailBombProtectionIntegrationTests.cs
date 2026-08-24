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
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// SEC-NEW: anti-email-bomb dla OTP. Niezależnie od liczby appointmentów / tenantów / IP,
/// jeden target email NIE powinien dostać więcej niż N maili z OTP na godzinę.
///
/// Bez tego throttle attacker mógłby:
///   1. Stworzyć wiele appointmentów (1 hold = 1 nowy appointmentId)
///   2. Dla każdego appointmentu wywołać request-otp z target_email = "ofiara@gmail.com"
///   3. Każdy appointment ma osobny cooldown (60s) — bypass per-appointment limit
///   4. Wysłać 30+ maili/min do ofiary z 1 IP, więcej z botnetu — realny koszt ACS Email.
/// </summary>
public sealed class BookingOtpEmailBombProtectionIntegrationTests
{
  private const string TargetVictimEmail = "victim-bomb-test@example.local";

  // Rozdzielacz slotów dla SeedFreshAppointment — patrz komentarz przy tym helperze.
  // Startuje od -1, bo Interlocked.Increment zwraca wartość PO inkrementacji (pierwszy slot = 0).
  private static int _slotCounter = -1;

  [Fact]
  public async Task Same_target_email_blocked_after_max_per_hour_across_multiple_appointments()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      // Podnosimy rate-limity, żeby testować WŁAŚCIWIE throttle per-email (nie per-IP).
      builder.UseSetting("RateLimiting:Global:PermitLimit", "10000");
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "10000");
    });
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Tworzymy 10 osobnych appointmentów (każdy z własnym lease tokenem) i dla każdego
    // wywołujemy request-otp z TYM SAMYM target email. Bez ochrony — 10 maili by poszło.
    // Z ochroną (cap 5/godz per email) — DOKŁADNIE 5 powinno przejść, reszta 429.
    var sentCount = 0;
    var rejectedCount = 0;
    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 10; i++)
    {
      var appointmentId = SeedFreshAppointment(factory.Services, seed, out var leaseToken);
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp",
        new
        {
          token = leaseToken,
          email = TargetVictimEmail,
          phoneNumber = (string?)null,
        },
        ct);
      statuses.Add(resp.StatusCode);
      if (resp.StatusCode == HttpStatusCode.OK)
      {
        sentCount++;
      }
      else if (resp.StatusCode == HttpStatusCode.TooManyRequests)
      {
        rejectedCount++;
      }
    }

    // Cap = 5 (MaxOtpPerEmailPerHour w BookingOtpProtectionService)
    Assert.Equal(5, sentCount);
    Assert.Equal(5, rejectedCount);

    // Mailbox potwierdza: dokładnie 5 maili wysłanych do tej ofiary, mimo 10 prób.
    var sentToVictim = mailbox.AllSent
      .Count(s => s.ToEmail.Equals(TargetVictimEmail, StringComparison.OrdinalIgnoreCase));
    Assert.Equal(5, sentToVictim);
  }

  [Fact]
  public async Task Throttle_is_per_email_so_different_targets_not_blocked()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:Global:PermitLimit", "10000");
      builder.UseSetting("RateLimiting:PublicBookingWrite:PermitLimit", "10000");
    });
    _ = factory.Services;
    var seed = SeedPerfTenant(factory.Services);
    var mailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // 6 RÓŻNYCH adresów — każdy powinien przejść (per-email throttle dotyczy tylko POJEDYNCZEGO adresu)
    var sentCount = 0;
    for (var i = 0; i < 6; i++)
    {
      var appointmentId = SeedFreshAppointment(factory.Services, seed, out var leaseToken);
      var resp = await client.PostAsJsonAsync(
        $"/api/booking/{seed.Slug}/public-appointment/{appointmentId}/request-otp",
        new
        {
          token = leaseToken,
          email = $"different-target-{i}@example.local",
          phoneNumber = (string?)null,
        },
        ct);
      if (resp.IsSuccessStatusCode) sentCount++;
    }

    Assert.Equal(6, sentCount);
    Assert.Equal(6, mailbox.AllSent.Count);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────

  private sealed record PerfSeed(string Slug, Guid TenantId, Guid EmployeeId, Guid ServiceId);

  private static PerfSeed SeedPerfTenant(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var slug = "bomb-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var tenant = new Tenant("Bomb Test Salon", slug);
    // Ten test sprawdza throttle maili z OTP — pinujemy kanał na Email (domyślny to teraz Phone/SMS).
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "B", "T", "b@t.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "S", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, 30, new Money(50m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.SaveChanges();

    return new PerfSeed(slug, tenant.Id, employee.Id, service.Id);
  }

  private static Guid SeedFreshAppointment(IServiceProvider rootServices, PerfSeed seed, out Guid leaseToken)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));

    // Slot MUSI być unikalny per (EmployeeId, Date, StartTime) — pilnuje tego unikalny indeks
    // IX_Appointments_EmployeeId_Date_StartTime (ochrona przed podwójną rezerwacją).
    //
    // Było tu losowanie: Random.Next(7,90) dni × Random.Next(8,16) godzin = 664 kombinacje,
    // przy 10 wywołaniach w pętli. Problem urodzinowy daje ~6,5% szans na kolizję, więc CI
    // wywracało się średnio co kilkanaście deployów — losowo, przy niepowiązanej zmianie
    // (Npgsql 23505). InMemory nie egzekwuje unikalnych indeksów, więc widać to WYŁĄCZNIE
    // w przebiegu na PostgreSQL.
    //
    // Licznik rozdziela sloty deterministycznie i rozłącznie. Zwykły Random z ustalonym
    // seedem by NIE wystarczył — Random.Shared jest współdzielony między testami biegnącymi
    // równolegle. Zakres dni 7..89 zostaje taki jak był, więc mieści się w horyzoncie rezerwacji.
    var slot = Interlocked.Increment(ref _slotCounter);
    var startHour = 8 + (slot % 8); // 8..15
    var dayOffset = 7 + (slot / 8) % 83; // 7..89

    var appointment = new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      customerId: null,
      DateOnly.FromDateTime(DateTime.UtcNow).AddDays(dayOffset),
      new TimeOnly(startHour, 0),
      new TimeOnly(startHour, 30),
      AppointmentStatus.Pending,
      new Money(50m, "PLN"),
      string.Empty,
      lease);

    db.Appointments.Add(appointment);
    db.SaveChanges();

    return appointment.Id;
  }
}
