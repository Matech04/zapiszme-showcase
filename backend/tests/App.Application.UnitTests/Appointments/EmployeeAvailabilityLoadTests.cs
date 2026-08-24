using App.Application.Appointments.Queries.GetAvailableTimeSlots;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// HIGH (eager-load) + M11 (N+1 usług): <see cref="EmployeeRepository.GetForAvailabilityAsync"/>
/// ładuje override/urlopy ograniczone do żądanego zakresu, a wyliczanie czasu combo pobiera
/// usługi jednym zapytaniem (z walidacją kompletności).
/// </summary>
public sealed class EmployeeAvailabilityLoadTests
{
  // UWAGA: EF InMemory NIE honoruje predykatu filtered-include (.Include(x => x.Coll.Where(...)))
  // — materializuje całą kolekcję. Ścisła materializacja "tylko w zakresie" jest własnością providera
  // relacyjnego (Npgsql) i jest weryfikowana w integration tests (Testcontainers). Tu weryfikujemy
  // REGRESJĘ FUNKCJONALNĄ: dostępność liczy się poprawnie dla dnia z override w zakresie oraz
  // dla dnia z urlopem (zero slotów), niezależnie od trybu materializacji.

  [Fact]
  public async Task Availability_reflects_override_for_day_in_range()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SeedAsync(ct);

    // Wybierz dzień roboczy w przyszłości i zawęź jego grafik override'em (9–10 zamiast 8–16).
    var day = NextWorkday();
    employee.SetScheduleOverride(day, new List<TimeRange> { new(new TimeOnly(9, 0), new TimeOnly(10, 0)) });
    db.Employees.Update(employee);
    await db.SaveChangesAsync(ct);

    var handler = BuildHandler(db, tenant.Id);

    // Bez override (inny dzień roboczy) — pełne okno 8–16. Z override — wąskie okno 9–10.
    var anotherDay = day.AddDays(7);
    while (anotherDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
    {
      anotherDay = anotherDay.AddDays(1);
    }

    var overriddenSlots = await handler.Handle(new GetAvailableTimeSlotsQuery(day, employee.Id, [service.Id]), ct);
    var normalSlots = await handler.Handle(new GetAvailableTimeSlotsQuery(anotherDay, employee.Id, [service.Id]), ct);

    Assert.NotEmpty(overriddenSlots);
    // Wąskie okno 9–10 (60 min, usługa 30 min) daje wyraźnie mniej slotów niż pełny dzień 8–16.
    Assert.True(overriddenSlots.Count < normalSlots.Count,
      "Override zawężający dzień musi ograniczyć liczbę slotów.");
    // Żaden slot nie może zaczynać się poza oknem override (przed 9:00).
    Assert.DoesNotContain(overriddenSlots, s => TimeOnly.Parse(s.slot) < new TimeOnly(9, 0));
  }

  [Fact]
  public async Task Availability_is_empty_for_day_covered_by_leave_in_range()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SeedAsync(ct);

    var day = NextWorkday();
    employee.AddLeave(day, day);
    db.Employees.Update(employee);
    await db.SaveChangesAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var slots = await handler.Handle(new GetAvailableTimeSlotsQuery(day, employee.Id, [service.Id]), ct);

    Assert.Empty(slots);
  }

  [Fact]
  public async Task Combo_with_multiple_services_sums_durations_in_a_single_pass()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SeedAsync(ct);

    // Druga usługa combo (60 min) — łączny czas 30 + 60 = 90 min.
    var category = await db.ServiceCategories.FirstAsync(ct);
    var vat = await db.VatRates.FirstAsync(ct);
    var service2 = new Service(tenant.Id, category.Id, vat.Id, "Koloryzacja", new Money(120m, "PLN"), 60);
    db.Services.Add(service2);
    employee.AssignService(tenant.Id, service2.Id, service2.DurationInMinutes, new Money(120m, "PLN"));
    db.Employees.Update(employee);
    await db.SaveChangesAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var monday = NextWorkday();

    var single = await handler.Handle(new GetAvailableTimeSlotsQuery(monday, employee.Id, [service.Id]), ct);
    var combo = await handler.Handle(
      new GetAvailableTimeSlotsQuery(monday, employee.Id, [service.Id, service2.Id]), ct);

    // Combo (90 min) mieści mniej slotów w tym samym oknie pracy niż pojedyncza (30 min) usługa.
    Assert.NotEmpty(single);
    Assert.NotEmpty(combo);
    Assert.True(combo.Count < single.Count, "Dłuższe combo musi dawać mniej slotów niż pojedyncza usługa.");
  }

  [Fact]
  public async Task Combo_with_unknown_service_throws_NotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SeedAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var monday = NextWorkday();

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(
        new GetAvailableTimeSlotsQuery(monday, employee.Id, [service.Id, Guid.NewGuid()]), ct));
  }

  private static GetAvailableTimeSlotsHandler BuildHandler(ApplicationDbContext db, Guid tenantId) =>
    new(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

  private static DateOnly NextWorkday()
  {
    var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
    while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
    {
      d = d.AddDays(1);
    }
    return d;
  }

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SeedAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Avail Salon", "avail-" + Guid.NewGuid().ToString("N")[..8]);
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenant.Id));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Mon-Fri", "Worker", "mon-fri@avail.local");
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

    return (db, tenant, employee, service);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
