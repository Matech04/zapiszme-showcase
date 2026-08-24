using App.Application.Appointments.Queries.GetAppointmentMonthAvailability;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Panel salonu — kolorowanie dni w datepickerze (liczba wolnych slotów / miesiąc).
/// </summary>
public sealed class GetAppointmentMonthAvailabilityTests
{
  [Fact]
  public async Task Returns_slot_counts_per_day_workdays_have_slots_weekend_is_empty()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Staff Month Avail", "staff-month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Mon-Fri", "Worker", "mon-fri@staff-month.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);

    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var weekly = new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
      [DayOfWeek.Monday] = workRanges,
      [DayOfWeek.Tuesday] = workRanges,
      [DayOfWeek.Wednesday] = workRanges,
      [DayOfWeek.Thursday] = workRanges,
      [DayOfWeek.Friday] = workRanges,
    };
    employee.SetWeeklySchedule(weekly);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    await db.SaveChangesAsync(ct);

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]),
      ct);

    Assert.Equal(DateTime.DaysInMonth(target.Year, target.Month), result.Count);

    var monday = result.First(d => d.date.DayOfWeek == DayOfWeek.Monday);
    Assert.True(monday.availableCount > 0, "Dzień roboczy powinien mieć wolne sloty.");

    var sunday = result.First(d => d.date.DayOfWeek == DayOfWeek.Sunday);
    Assert.Equal(0, sunday.availableCount);
  }

  [Fact]
  public async Task Returns_empty_when_month_is_out_of_range()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Bad Staff Month", "bad-staff-month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(2026, 13, Guid.NewGuid(), [Guid.NewGuid()]),
      ct);

    Assert.Empty(result);
  }

  [Fact]
  public async Task Fixed_override_day_has_slots_even_without_weekly_schedule_per_day_mode()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("PerDay Month", "perday-month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    // Pracownik globalnie w trybie SIATKI, BEZ grafiku tygodniowego.
    var employee = new Employee(tenant.Id, null, "Paper", "Calendar", "paper@perday.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
    var overrideDate = new DateOnly(target.Year, target.Month, 15);
    // Wyjątek STAŁY na 15. — tryb per-dzień wygrywa z globalnym trybem siatki.
    employee.SetScheduleOverride(overrideDate,
      new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    await db.SaveChangesAsync(ct);

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]),
      ct);

    // Dzień z wyjątkiem stałym ma dokładnie 3 sloty (9/12/15, brak kolizji, gap-filling pominięty w fixed).
    var day15 = result.First(d => d.date == overrideDate);
    Assert.Equal(3, day15.availableCount);

    // Inny dzień (brak grafiku i brak override) → 0 (pracownik siatkowy bez grafiku).
    var other = result.First(d => d.date != overrideDate && d.date.Day == 16);
    Assert.Equal(0, other.availableCount);
  }

  [Fact]
  public async Task Expired_awaiting_otp_hold_does_not_reduce_day_availability()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Hold Month", "hold-month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Hold", "Worker", "hold@month.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
    var overrideDate = new DateOnly(target.Year, target.Month, 15);
    // Tryb stały: dokładnie 3 sloty (9/12/15), gap-filling pominięty → liczba deterministyczna.
    employee.SetScheduleOverride(overrideDate,
      new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }));

    // Anonimowy hold OTP na 9:00 z WYGASŁYM lease — squatter, który nie dokończył weryfikacji.
    // Nie powinien blokować slotu (job sprzątający i tak go usunie), więc dzień nadal ma 3 wolne sloty.
    var expiredHold = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      overrideDate, new TimeOnly(9, 0), new TimeOnly(10, 0),
      AppointmentStatus.AwaitingOtp, new Money(120m, "PLN"), "",
      lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5)),
      source: AppointmentSource.Online);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(expiredHold);
    await db.SaveChangesAsync(ct);

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]),
      ct);

    var day15 = result.First(d => d.date == overrideDate);
    Assert.Equal(3, day15.availableCount);
  }

  [Fact]
  public async Task Active_awaiting_otp_hold_blocks_overlapping_slot()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Active Hold Month", "active-hold-month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Active", "Worker", "active@month.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
    var overrideDate = new DateOnly(target.Year, target.Month, 15);
    employee.SetScheduleOverride(overrideDate,
      new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }));

    // Aktywny hold OTP (lease wciąż ważny) na 9:00 — blokuje slot: zostają 2 wolne.
    var activeHold = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      overrideDate, new TimeOnly(9, 0), new TimeOnly(10, 0),
      AppointmentStatus.AwaitingOtp, new Money(120m, "PLN"), "",
      lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5)),
      source: AppointmentSource.Online);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(activeHold);
    await db.SaveChangesAsync(ct);

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]),
      ct);

    var day15 = result.First(d => d.date == overrideDate);
    Assert.Equal(2, day15.availableCount);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
