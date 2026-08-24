using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Common;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

/// <summary>
/// Backdoor endpointy dla Playwright E2E. AKTYWNE TYLKO w env ASPNETCORE_ENVIRONMENT=E2E
/// — każda akcja sprawdza <see cref="IHostEnvironment.IsEnvironment"/> i zwraca 404
/// poza tym środowiskiem. Endpoint nie pojawia się też w Swagger (IgnoreApi).
/// </summary>
[ApiController]
[Route("api/_e2e")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class E2eController : ControllerBase
{
  private readonly IHostEnvironment _env;
  private readonly IServiceProvider _services;

  public E2eController(IHostEnvironment env, IServiceProvider services)
  {
    _env = env;
    _services = services;
  }

  private IActionResult? GuardEnvironment()
  {
    return _env.IsEnvironment("E2E") ? null : NotFound();
  }

  // ─── SEED ───────────────────────────────────────────────────────────────────

  [HttpPost("seed")]
  public async Task<IActionResult> Seed(CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;

    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var existing = await db.Tenants
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Slug == RestApiIntegrationSeed.TenantSlug, ct);

    if (existing is not null)
    {
      // Re-seed jest RESTORATYWNY: wspólny tenant jest mutowany przez inne specy (soft-delete,
      // unassign usług, zmiana ustawień), a fixture woła /seed przed każdym testem — więc tutaj
      // przywracamy znany-dobry stan, inaczej np. holdy publiczne padają na EmployeeServiceMissing.
      var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == existing.Id, ct);

      // Wyczyść NAGROMADZONE wizyty z poprzednich testów/przebiegów. Baza booking_e2e jest trwała,
      // a anulowane/wygasłe wizyty zostawiają wiersze blokujące unikalny indeks
      // (EmployeeId, Date, StartTime) — available-slots oferuje wtedy slot, ale hold pada na
      // duplicate key. Twardy delete (dane testowe, brak referencji wychodzących).
      await db.Appointments.IgnoreQueryFilters()
        .Where(a => a.TenantId == existing.Id)
        .ExecuteDeleteAsync(ct);

      var employeesTracked = await db.Employees.IgnoreQueryFilters().Include(e => e.Services)
        .Where(e => e.TenantId == existing.Id).ToListAsync(ct);
      var customersTracked = await db.Customers.IgnoreQueryFilters()
        .Where(c => c.TenantId == existing.Id).ToListAsync(ct);
      var servicesTracked = await db.Services.IgnoreQueryFilters()
        .Where(s => s.TenantId == existing.Id).ToListAsync(ct);

      // Re-activate soft-deleted entities (kolejne testy mogły je dezaktywować).
      foreach (var entity in employeesTracked.Cast<global::ISoftDelete>()
        .Concat(customersTracked.Cast<global::ISoftDelete>())
        .Concat(servicesTracked.Cast<global::ISoftDelete>()))
      {
        if (!entity.IsActive)
        {
          // ISoftDelete.IsActive jest read-only z setterem private — używamy reflection.
          var prop = entity.GetType().GetProperty("IsActive");
          prop?.SetValue(entity, true);
        }
      }

      // Przywróć bazowe ustawienia bookingu (testy mogą zmieniać kanał / require-name / polityki).
      tenant.Update(
        tenant.Name,
        tenant.Slug,
        customerVerificationChannel: CustomerVerificationChannel.Email,
        bookingAccessPolicy: BookingAccessPolicy.Open,
        appointmentConfirmationMode: AppointmentConfirmationMode.Automatic,
        requireCustomerName: false);

      // Zasiany owner-pracownik i usługa "Strzyżenie" (deterministycznie, niezależnie od encji z innych specy).
      var ownerEmployee = employeesTracked.FirstOrDefault(e => e.UserId == IntegrationTestUserIds.SalonOwner)
        ?? employeesTracked.First();
      var seedService = servicesTracked.First();

      // Przywróć powiązanie pracownik↔usługa — specy CRUD usług/pracowników je usuwają,
      // a publiczny hold liczy cenę przez Employee.CalculateTotalPrice → EmployeeServiceMissing.
      if (!ownerEmployee.Services.Any(s => s.ServiceId == seedService.Id))
      {
        ownerEmployee.AssignService(existing.Id, seedService.Id, seedService.DurationInMinutes, seedService.Price);
      }

      await db.SaveChangesAsync(ct);

      var category = await db.ServiceCategories.IgnoreQueryFilters().AsNoTracking()
        .FirstAsync(c => c.TenantId == existing.Id, ct);
      var vat = await db.VatRates.IgnoreQueryFilters().AsNoTracking()
        .FirstAsync(v => v.TenantId == existing.Id, ct);
      var customer = customersTracked.FirstOrDefault(c => c.Email == "jan@rest-seed.local")
        ?? customersTracked.First();

      return Ok(new
      {
        tenantId = existing.Id,
        tenantSlug = existing.Slug,
        employeeId = ownerEmployee.Id,
        serviceId = seedService.Id,
        serviceCategoryId = category.Id,
        vatRateId = vat.Id,
        customerId = customer.Id,
        ownerUserId = IntegrationTestUserIds.SalonOwner,
        ownerEmail = "owner@rest-seed.local",
        reused = true,
      });
    }

    var result = RestApiIntegrationSeed.Seed(_services);
    return Ok(new
    {
      tenantId = result.TenantId,
      tenantSlug = result.TenantSlug,
      employeeId = result.EmployeeId,
      serviceId = result.ServiceId,
      serviceCategoryId = result.ServiceCategoryId,
      vatRateId = result.VatRateId,
      customerId = result.CustomerId,
      ownerUserId = IntegrationTestUserIds.SalonOwner,
      ownerEmail = "owner@rest-seed.local",
      reused = false,
    });
  }

  [HttpPost("seed/second-tenant")]
  public IActionResult SeedSecondTenant()
  {
    if (GuardEnvironment() is { } block) return block;
    var result = RestApiIntegrationSeed.SeedSecondTenant(_services);
    return Ok(new
    {
      tenantId = result.TenantId,
      tenantSlug = result.TenantSlug,
      employeeId = result.EmployeeId,
      ownerUserId = IntegrationTestUserIds.SecondSalonOwner,
    });
  }

  // ─── MAILBOX ────────────────────────────────────────────────────────────────

  [HttpGet("auth-email/{email}")]
  public IActionResult GetLastAuthEmail(string email)
  {
    if (GuardEnvironment() is { } block) return block;
    var mailbox = _services.GetRequiredService<TestAuthEmailMailbox>();
    var normalized = email.Trim().ToLowerInvariant();
    var lastConfirm = mailbox.ConfirmEmailsSent
      .Where(e => string.Equals(e.ToEmail, normalized, StringComparison.OrdinalIgnoreCase))
      .Select(e => (string?)e.Url)
      .LastOrDefault();
    var lastReset = mailbox.PasswordResetsSent
      .Where(e => string.Equals(e.ToEmail, normalized, StringComparison.OrdinalIgnoreCase))
      .Select(e => (string?)e.Url)
      .LastOrDefault();
    return Ok(new
    {
      lastConfirmEmailUrl = lastConfirm,
      lastPasswordResetUrl = lastReset,
      lastEmployeeInviteUrl = mailbox.LastEmployeeInviteUrl,
    });
  }

  [HttpGet("otp/{email}")]
  public IActionResult GetLastOtp(string email)
  {
    if (GuardEnvironment() is { } block) return block;
    var mailbox = _services.GetRequiredService<TestBookingOtpMailbox>();
    var normalized = email.Trim().ToLowerInvariant();
    var last = mailbox.AllSent
      .Where(e => string.Equals(e.ToEmail, normalized, StringComparison.OrdinalIgnoreCase))
      .Select(e => (object)new { code = e.Code, salonName = e.SalonName })
      .LastOrDefault();
    return last is null ? NotFound() : Ok(last);
  }

  /// <summary>
  /// Ostatni kod SMS OTP wysłany dla podanego usera. Backdoor mapuje userId → numer
  /// telefonu z bazy, potem wyciąga ostatni przechwycony kod z <see cref="TestPhoneOtpMailbox"/>.
  /// </summary>
  [HttpGet("phone-otp/{userId:guid}")]
  public async Task<IActionResult> GetLastPhoneOtp(Guid userId, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    var db = _services.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(u => u.Id == userId)
      .Select(u => new { u.PhoneNumber })
      .FirstOrDefaultAsync(ct);
    if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
    {
      return NotFound();
    }

    var mailbox = _services.GetRequiredService<TestPhoneOtpMailbox>();
    var code = mailbox.LastCodeForPhone(user.PhoneNumber);
    return code is null ? NotFound() : Ok(new { phone = user.PhoneNumber, code });
  }

  // ─── TIME ───────────────────────────────────────────────────────────────────

  [HttpPost("time/advance")]
  public IActionResult AdvanceTime([FromQuery] int seconds)
  {
    if (GuardEnvironment() is { } block) return block;
    if (seconds <= 0) return BadRequest(new { error = "seconds must be > 0" });
    var clock = _services.GetRequiredService<E2eTimeProvider>();
    clock.Advance(TimeSpan.FromSeconds(seconds));
    return Ok(new { utcNow = clock.GetUtcNow() });
  }

  /// <summary>
  /// Resetuje zegar do bieżącego real-time. <see cref="E2eTimeProvider"/> pozwala cofać czas,
  /// więc reset jest niezawodny niezależnie od wcześniejszych advance w innych specach
  /// (np. TC-P011 advance 10s; bez resetu kolejne testy widziałyby przesunięty zegar).
  /// </summary>
  [HttpPost("time/reset")]
  public IActionResult ResetTime()
  {
    if (GuardEnvironment() is { } block) return block;
    var clock = _services.GetRequiredService<E2eTimeProvider>();
    clock.SetUtcNow(DateTimeOffset.UtcNow);
    return Ok(new { utcNow = clock.GetUtcNow() });
  }

  // ─── BACKGROUND JOBS ────────────────────────────────────────────────────────

  [HttpPost("bg/run-reminders")]
  public async Task<IActionResult> RunReminders(CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    var svc = _services.GetRequiredService<AppointmentReminderHostedService>();
    await svc.ExecuteOnceAsync(ct);
    return Ok(new { ran = "reminders" });
  }

  [HttpPost("bg/run-status-lifecycle")]
  public async Task<IActionResult> RunStatusLifecycle(CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    var svc = _services.GetRequiredService<AppointmentStatusLifecycleHostedService>();
    await svc.ExecuteOnceAsync(ct);
    return Ok(new { ran = "status-lifecycle" });
  }

  [HttpPost("bg/run-cleanup-unconfirmed")]
  public async Task<IActionResult> RunCleanupUnconfirmed(CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    var svc = _services.GetRequiredService<UnconfirmedAccountCleanupHostedService>();
    await svc.ExecuteOnceAsync(ct);
    return Ok(new { ran = "cleanup-unconfirmed" });
  }

  // ─── SPRINT E: ROZSZERZENIA SEEDU + TOKEN MANIPULATION ──────────────────────

  /// <summary>
  /// Tworzy User EmailConfirmed=false z CreatedAt w przeszłości. Pozwala TC-A010
  /// (cleanup BG) i TC-A014 (login niepotwierdzony) testować bez czekania.
  /// </summary>
  public sealed record SeedUnconfirmedUserRequest(string Email, double AgeHours = 49);

  [HttpPost("seed-unconfirmed-user")]
  public async Task<IActionResult> SeedUnconfirmedUser([FromBody] SeedUnconfirmedUserRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var email = req.Email.Trim().ToLowerInvariant();
    var existing = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);
    if (existing is not null)
    {
      return Ok(new { userId = existing.Id, reused = true });
    }

    var newUser = new User(email, "E2E Unconfirmed")
    {
      NormalizedUserName = email.ToUpperInvariant(),
      NormalizedEmail = email.ToUpperInvariant(),
      EmailConfirmed = false,
      SecurityStamp = Guid.NewGuid().ToString(),
    };
    newUser.PasswordHash = new PasswordHasher<User>().HashPassword(newUser, "Password123!");

    // CreatedAt ma private setter — ustawiamy via reflection (tylko w env E2E).
    var createdAtProp = typeof(App.Domain.Aggregates.UserAggregate.User).GetProperty("CreatedAt")!;
    createdAtProp.SetValue(newUser, DateTime.UtcNow.AddHours(-req.AgeHours));

    db.Users.Add(newUser);
    await db.SaveChangesAsync(ct);
    return Ok(new { userId = newUser.Id, email = newUser.Email, createdAt = newUser.CreatedAt, reused = false });
  }

  /// <summary>
  /// Tworzy weekly schedule dla pracownika (pon-pt 09:00-17:00). Pozwala testom
  /// TC-D006/D007/booking pickerom na realne available slots.
  /// </summary>
  public sealed record SeedScheduleRequest(Guid EmployeeId, TimeOnly Start = default, TimeOnly End = default);

  [HttpPost("seed-employee-schedule")]
  public async Task<IActionResult> SeedEmployeeSchedule([FromBody] SeedScheduleRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var employee = await db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct);
    if (employee is null) return NotFound(new { error = "Employee not found" });

    var start = req.Start == default ? new TimeOnly(9, 0) : req.Start;
    var end = req.End == default ? new TimeOnly(17, 0) : req.End;
    var workRange = new TimeRange(start, end);

    var weekly = new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
      [DayOfWeek.Monday] = new[] { workRange },
      [DayOfWeek.Tuesday] = new[] { workRange },
      [DayOfWeek.Wednesday] = new[] { workRange },
      [DayOfWeek.Thursday] = new[] { workRange },
      [DayOfWeek.Friday] = new[] { workRange },
      [DayOfWeek.Saturday] = Array.Empty<TimeRange>(),
      [DayOfWeek.Sunday] = Array.Empty<TimeRange>(),
    };

    // Reset trybu do siatki — chroni kolejne specy na współdzielonym tenancie przed wyciekiem
    // trybu FixedStartTimes z seed-employee-fixed-schedule (grid + tryb fixed = brak slotów).
    employee.SetSlotGenerationMode(SlotGenerationMode.Grid);
    employee.SetWeeklySchedule(weekly);
    await db.SaveChangesAsync(ct);
    return Ok(new { employeeId = req.EmployeeId, schedule = $"{start:HH:mm}-{end:HH:mm} mon-fri" });
  }

  /// <summary>
  /// Ustawia pracownika w trybie stałych slotów (FixedStartTimes) z podanymi godzinami startu
  /// dla pon-pt. Pozwala specom e2e na realny grafik fixed bez przechodzenia przez UI panelu.
  /// Domyślne godziny: 09:00, 12:00, 15:00.
  /// </summary>
  public sealed record SeedFixedScheduleRequest(Guid EmployeeId, TimeOnly[]? StartTimes = null);

  [HttpPost("seed-employee-fixed-schedule")]
  public async Task<IActionResult> SeedEmployeeFixedSchedule([FromBody] SeedFixedScheduleRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var employee = await db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct);
    if (employee is null) return NotFound(new { error = "Employee not found" });

    var times = req.StartTimes is { Length: > 0 }
      ? req.StartTimes
      : new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) };

    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);

    // Pon-pt te same stałe godziny (cycleIndex = (int)DayOfWeek; Sunday=0..Saturday=6).
    var workingDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
    var days = workingDays
      .Select(d => new ScheduleDay(times.ToList(), cycleIndex: (int)d))
      .ToList();
    employee.SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days);

    await db.SaveChangesAsync(ct);
    return Ok(new
    {
      employeeId = req.EmployeeId,
      mode = "FixedStartTimes",
      startTimes = times.Select(t => t.ToString("HH:mm")).ToArray(),
    });
  }

  public sealed record SeedLeaveRequest(Guid EmployeeId, DateOnly StartDate, DateOnly EndDate);

  [HttpPost("seed-employee-leave")]
  public async Task<IActionResult> SeedEmployeeLeave([FromBody] SeedLeaveRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = await db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct);
    if (employee is null) return NotFound(new { error = "Employee not found" });
    employee.AddLeave(req.StartDate, req.EndDate);
    await db.SaveChangesAsync(ct);
    return Ok(new { employeeId = req.EmployeeId, from = req.StartDate, to = req.EndDate });
  }

  /// <summary>
  /// Tworzy wizytę w określonym statusie. Używane przez testy które muszą zacząć
  /// z konkretną wizytą (status flow TC-D011-013, cancel TC-D010, reschedule TC-D008).
  /// </summary>
  public sealed record SeedAppointmentRequest(
    Guid EmployeeId,
    Guid ServiceId,
    Guid CustomerId,
    DateOnly Date,
    TimeOnly StartTime,
    string Status = "Booked",
    int DurationMinutes = 30,
    decimal PriceAmount = 100,
    string Currency = "PLN");

  [HttpPost("seed-appointment")]
  public async Task<IActionResult> SeedAppointment([FromBody] SeedAppointmentRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var employee = await db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct);
    if (employee is null) return NotFound(new { error = "Employee not found" });

    var status = req.Status.ToLowerInvariant() switch
    {
      "pending" => AppointmentStatus.Pending,
      "awaitingotp" => AppointmentStatus.AwaitingOtp,
      "booked" => AppointmentStatus.Booked,
      "inprogress" => AppointmentStatus.InProgress,
      "completed" => AppointmentStatus.Completed,
      "canceled" or "cancelled" => AppointmentStatus.Canceled,
      _ => AppointmentStatus.Booked,
    };

    var endTime = req.StartTime.AddMinutes(req.DurationMinutes);
    var price = new Money(req.PriceAmount, req.Currency);

    var appointment = new Appointment(
      employee.TenantId,
      req.EmployeeId,
      req.ServiceId,
      req.CustomerId,
      req.Date,
      req.StartTime,
      endTime,
      status,
      price,
      string.Empty,
      null,
      AppointmentSource.Panel);

    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);
    return Ok(new { appointmentId = appointment.Id, status = status.Name });
  }

  /// <summary>
  /// Wymusza wygaśnięcie reset-password tokenu poprzez rotację SecurityStamp.
  /// Następne użycie wcześniej-otrzymanego linku zwróci 400.
  /// </summary>
  public sealed record ExpireTokenRequest(string Email);

  [HttpPost("expire-reset-token")]
  public async Task<IActionResult> ExpireResetToken([FromBody] ExpireTokenRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var user = await um.FindByEmailAsync(req.Email);
    if (user is null) return NotFound(new { error = "User not found" });
    await um.UpdateSecurityStampAsync(user);
    return Ok(new { email = req.Email, securityStampRotated = true });
  }

  /// <summary>
  /// Wymusza wygaśnięcie confirm-email tokenu (ta sama mechanika — rotacja SecurityStamp).
  /// </summary>
  [HttpPost("expire-confirm-token")]
  public Task<IActionResult> ExpireConfirmToken([FromBody] ExpireTokenRequest req, CancellationToken ct)
    => ExpireResetToken(req, ct);

  /// <summary>
  /// Tworzy drugiego Employee w seeded tenancie z User account (login). Pozwala TC-A012
  /// (Employee login → /admin/schedule) testować bez UI invite/accept.
  /// </summary>
  public sealed record SeedEmployeeWithCredentialsRequest(string Email, string Role = "Employee");

  [HttpPost("seed-employee-with-credentials")]
  public async Task<IActionResult> SeedEmployeeWithCredentials([FromBody] SeedEmployeeWithCredentialsRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = await db.Tenants.IgnoreQueryFilters()
      .FirstOrDefaultAsync(t => t.Slug == RestApiIntegrationSeed.TenantSlug, ct);
    if (tenant is null) return BadRequest(new { error = "Run /api/_e2e/seed first" });

    var email = req.Email.Trim().ToLowerInvariant();
    var existing = await db.Users.IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);
    if (existing is not null)
    {
      var existingEmp = await db.Employees.IgnoreQueryFilters()
        .FirstOrDefaultAsync(e => e.UserId == existing.Id, ct);
      return Ok(new { userId = existing.Id, employeeId = existingEmp?.Id, reused = true });
    }

    var newUser = new App.Domain.Aggregates.UserAggregate.User(email, "E2E Employee")
    {
      NormalizedUserName = email.ToUpperInvariant(),
      NormalizedEmail = email.ToUpperInvariant(),
      EmailConfirmed = true,
      // SMS-first: login ma drugi gate (AuthController) — bez potwierdzonego telefonu
      // przekierowuje na /confirm-phone. Seedowany pracownik ma od razu logować się
      // do /admin, więc telefon ustawiamy jako potwierdzony.
      PhoneNumber = "+48500100200",
      PhoneNumberConfirmed = true,
      SecurityStamp = Guid.NewGuid().ToString(),
    };
    newUser.PasswordHash = new PasswordHasher<App.Domain.Aggregates.UserAggregate.User>()
      .HashPassword(newUser, "Password123!");
    db.Users.Add(newUser);

    var roleId = await db.Roles
      .Where(r => r.NormalizedName == req.Role.ToUpperInvariant())
      .Select(r => r.Id)
      .FirstOrDefaultAsync(ct);
    if (roleId != Guid.Empty)
    {
      db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = newUser.Id, RoleId = roleId });
    }

    var employee = new App.Domain.Aggregates.EmployeeAggregate.Employee(tenant.Id, newUser.Id, "E2E", "Worker", email);
    db.Employees.Add(employee);
    await db.SaveChangesAsync(ct);

    return Ok(new { userId = newUser.Id, employeeId = employee.Id, email = newUser.Email, reused = false });
  }

  /// <summary>
  /// Toggle whitelist na seeded customerze. Dla TC-D030/D031.
  /// </summary>
  public sealed record SetCustomerWhitelistRequest(Guid CustomerId, bool IsWhitelisted);

  [HttpPost("set-customer-whitelist")]
  public async Task<IActionResult> SetCustomerWhitelist([FromBody] SetCustomerWhitelistRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var customer = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct);
    if (customer is null) return NotFound();
    // Customer ma SetWhitelist lub property publicznie; jeśli nie, używamy reflection.
    var prop = typeof(App.Domain.Aggregates.CustomerAggregate.Customer).GetProperty("IsWhitelisted");
    if (prop is null) return BadRequest(new { error = "Customer.IsWhitelisted property not found" });
    prop.SetValue(customer, req.IsWhitelisted);
    await db.SaveChangesAsync(ct);
    return Ok(new { customerId = customer.Id, isWhitelisted = req.IsWhitelisted });
  }

  /// <summary>
  /// Update salon settings (BookingAccessPolicy, ConfirmationMode, GapFilling).
  /// Dla TC-D043-046.
  /// </summary>
  public sealed record UpdateTenantSettingsRequest(
    string? BookingAccessPolicy = null,
    string? ConfirmationMode = null,
    string? CustomerVerificationChannel = null,
    string? StaffCalendarVisibilityPolicy = null,
    bool? RequireCustomerName = null);

  [HttpPost("set-tenant-settings")]
  public async Task<IActionResult> SetTenantSettings([FromBody] UpdateTenantSettingsRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == RestApiIntegrationSeed.TenantSlug, ct);
    if (tenant is null) return BadRequest(new { error = "Run /api/_e2e/seed first" });

    var tenantType = typeof(App.Domain.Aggregates.TenantAggregate.Tenant);
    void SetEnum(string propName, string? raw)
    {
      if (raw is null) return;
      var prop = tenantType.GetProperty(propName);
      if (prop is null) return;
      var enumType = prop.PropertyType;
      try
      {
        var parsed = Enum.Parse(enumType, raw, ignoreCase: true);
        prop.SetValue(tenant, parsed);
      }
      catch { /* ignore unparseable values */ }
    }

    SetEnum("BookingAccessPolicy", req.BookingAccessPolicy);
    SetEnum("ConfirmationMode", req.ConfirmationMode);
    SetEnum("CustomerVerificationChannel", req.CustomerVerificationChannel);
    SetEnum("StaffCalendarVisibilityPolicy", req.StaffCalendarVisibilityPolicy);

    if (req.RequireCustomerName is { } requireCustomerName)
    {
      tenantType.GetProperty("RequireCustomerName")?.SetValue(tenant, requireCustomerName);
    }

    await db.SaveChangesAsync(ct);
    return Ok(new { tenantId = tenant.Id, applied = true });
  }

  /// <summary>
  /// Tworzy in-app notification dla seeded tenanta. Dla TC-N011/N012 (mark-read).
  /// </summary>
  public sealed record SeedNotificationRequest(string Message = "Test notification", string Type = "Generic");

  [HttpPost("seed-notification")]
  public async Task<IActionResult> SeedNotification([FromBody] SeedNotificationRequest req, CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == RestApiIntegrationSeed.TenantSlug, ct);
    if (tenant is null) return BadRequest(new { error = "Run /api/_e2e/seed first" });

    // Notification entity location: App.Domain.Aggregates.NotificationAggregate.Notification (lub Application).
    // Używamy reflection — żeby nie zależeć od dokładnej sygnatury ctora.
    var notifType = AppDomain.CurrentDomain.GetAssemblies()
      .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
      .FirstOrDefault(t => t.Name == "Notification" && t.Namespace?.Contains("Aggregate") == true);
    if (notifType is null) return BadRequest(new { error = "Notification type not found" });

    try
    {
      var ctor = notifType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
      var parameters = ctor.GetParameters();
      var args = parameters.Select(p =>
      {
        if (p.ParameterType == typeof(Guid)) return (object)tenant.Id;
        if (p.ParameterType == typeof(string)) return (object)(p.Name?.ToLower().Contains("type") == true ? req.Type : req.Message);
        if (p.ParameterType == typeof(Guid?)) return (object?)null!;
        if (p.HasDefaultValue) return p.DefaultValue!;
        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!;
      }).ToArray();
      var notif = ctor.Invoke(args);
      db.Add(notif);
      await db.SaveChangesAsync(ct);
      var idProp = notifType.GetProperty("Id");
      return Ok(new { notificationId = idProp?.GetValue(notif) });
    }
    catch (Exception ex)
    {
      return BadRequest(new { error = ex.Message });
    }
  }

  /// <summary>
  /// Usuwa cały seed (tenant + użytkownicy + dane). Pozwala testom które chcą
  /// pełnej izolacji odzyskać czysty stan między iteracjami.
  /// </summary>
  [HttpPost("cleanup")]
  public async Task<IActionResult> Cleanup(CancellationToken ct)
  {
    if (GuardEnvironment() is { } block) return block;
    using var scope = _services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Usuń seedowane tenanty (oba). Casacde delete usunie powiązane dane.
    foreach (var slug in new[] { RestApiIntegrationSeed.TenantSlug, RestApiIntegrationSeed.SecondTenantSlug })
    {
      var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == slug, ct);
      if (tenant is null) continue;

      var apps = await db.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == tenant.Id).ToListAsync(ct);
      db.Appointments.RemoveRange(apps);
      var custs = await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == tenant.Id).ToListAsync(ct);
      db.Customers.RemoveRange(custs);
      var emps = await db.Employees.IgnoreQueryFilters().Where(e => e.TenantId == tenant.Id).ToListAsync(ct);
      db.Employees.RemoveRange(emps);
      var svcs = await db.Services.IgnoreQueryFilters().Where(s => s.TenantId == tenant.Id).ToListAsync(ct);
      db.Services.RemoveRange(svcs);
      var cats = await db.ServiceCategories.IgnoreQueryFilters().Where(c => c.TenantId == tenant.Id).ToListAsync(ct);
      db.ServiceCategories.RemoveRange(cats);
      var vats = await db.VatRates.IgnoreQueryFilters().Where(v => v.TenantId == tenant.Id).ToListAsync(ct);
      db.VatRates.RemoveRange(vats);
      db.Tenants.Remove(tenant);
    }

    await db.SaveChangesAsync(ct);
    return Ok(new { cleaned = true });
  }
}
