using App.Application.Booking.BookingAppointments.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// Kalendarz klienta koloruje kafelki dat liczbą wolnych slotów. Handler liczy dostępność
/// dla każdego dnia miesiąca: dni robocze (grafik 8–16) mają wolne sloty, weekend = 0.
/// </summary>
public sealed class GetBookingMonthAvailabilityTests
{
  [Fact]
  public async Task Returns_slot_counts_per_day_workdays_have_slots_weekend_is_empty()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Month Avail Salon", "month-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Mon-Fri", "Worker", "mon-fri@month.local");
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

    // Miesiąc ~2 miesiące w przód — wszystkie dni są w przyszłości (brak filtrowania „dziś”).
    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);

    var handler = new GetBookingMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetBookingMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]),
      ct);

    Assert.False(result.isClosed, "Miesiąc bez jawnej publikacji nie jest zamknięty.");
    Assert.Equal(DateTime.DaysInMonth(target.Year, target.Month), result.days.Count);

    var monday = result.days.First(d => d.date.DayOfWeek == DayOfWeek.Monday);
    Assert.True(monday.availableCount > 0, "Dzień roboczy powinien mieć wolne sloty.");
    Assert.True(monday.isWorkingDay, "Poniedziałek jest w grafiku (pon–pt) → dzień roboczy.");

    var sunday = result.days.First(d => d.date.DayOfWeek == DayOfWeek.Sunday);
    Assert.Equal(0, sunday.availableCount);
    // Weekend poza grafikiem → NIE dzień roboczy. Front używa tego, by nie skakać przez puste
    // miesiące dla pracownika bez grafiku (odróżnienie „brak grafiku” od „miesiąc zajęty”).
    Assert.False(sunday.isWorkingDay, "Niedziela poza grafikiem → nie dzień roboczy.");
  }

  [Fact]
  public async Task Returns_empty_when_month_is_out_of_range()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Bad Month Salon", "badmonth-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var handler = new GetBookingMonthAvailabilityQueryHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetBookingMonthAvailabilityQuery(2026, 13, Guid.NewGuid(), [Guid.NewGuid()]),
      ct);

    Assert.Empty(result.days);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
