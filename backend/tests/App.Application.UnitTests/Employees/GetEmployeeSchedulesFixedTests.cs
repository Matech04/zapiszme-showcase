using App.Application.Common.Interfaces;
using App.Application.Employees.Queries.GetEmployeeSchedules;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// Odczyt grafiku (GetEmployeeSchedules) musi zwrócić tryb + stałe godziny — inaczej edytor po
/// przeładowaniu pokazałby grafik fixed jako pusty Grid i kolejny zapis by go skasował.
/// </summary>
public sealed class GetEmployeeSchedulesFixedTests
{
  [Fact]
  public async Task Returns_mode_and_fixed_times_for_fixed_schedule()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var employee = new Employee(tenantId, null, "Ala", "Nails", "ala@nails.local");
    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    employee.SetSchedule(
      new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31)),
      1,
      new List<ScheduleDay> { new(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0) }, cycleIndex: 1) });
    db.Employees.Add(employee);
    await db.SaveChangesAsync(ct);

    var handler = new GetEmployeeSchedulesQueryHandler(db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetEmployeeSchedulesQuery(employee.Id), ct);

    var schedule = Assert.Single(result);
    Assert.Equal(SlotGenerationMode.FixedStartTimes, schedule.SlotGenerationMode);
    var day = Assert.Single(schedule.Days);
    Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0) }, day.FixedStartTimes!);
    Assert.Empty(day.WorkRanges);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
