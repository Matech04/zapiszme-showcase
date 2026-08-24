using App.Application.Appointments.Queries.GetAvailableTimeSlots;
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
/// APP-EMP-001 — EMP-015 / APPT-004 (EdgeCase): GetAvailableTimeSlots dla pracownika
/// bez planu na wybrany dzień (np. Niedziela) zwraca pustą listę.
/// </summary>
public sealed class GetAvailableTimeSlotsEmptyScheduleTests
{
  [Fact]
  public async Task Returns_empty_when_employee_has_no_schedule_on_requested_day()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Empty Schedule Salon", "empty-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Mon-Fri", "Worker", "mon-fri@empty.local");
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

    var sunday = NextSunday();
    var handler = new GetAvailableTimeSlotsHandler(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAvailableTimeSlotsQuery(sunday, employee.Id, [service.Id]),
      ct);

    Assert.Empty(result);
  }

  private static DateOnly NextSunday()
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
    while (today.DayOfWeek != DayOfWeek.Sunday)
    {
      today = today.AddDays(1);
    }
    return today;
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
