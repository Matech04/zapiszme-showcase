using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Commands.SetScheduleOverride;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// Wyjątki w grafiku stałym: override zapisuje stałe godziny startu, a rozwiązywanie dostępności
/// (GetFixedStartTimes dla danej daty) zwraca godziny z override zamiast bazowego grafiku.
/// </summary>
public sealed class SetScheduleOverrideFixedTests
{
  [Fact]
  public async Task Fixed_override_persists_start_times_and_resolves_for_date()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var date = new DateOnly(2026, 8, 4);

    var result = await NewHandler(db, tenantId, employee.Id).Handle(
      new SetScheduleOverrideCommand(
        employee.Id, date,
        SlotGenerationMode: SlotGenerationMode.FixedStartTimes,
        FixedStartTimes: new[] { new TimeOnly(15, 0), new TimeOnly(9, 0) }),
      ct);

    Assert.Empty(result.AppointmentsOutsideSchedule);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    var ovr = Assert.Single(reloaded!.Overrides);
    Assert.True(ovr.IsFixed);
    Assert.Empty(ovr.WorkRanges);
    // Sort + rozwiązanie per data.
    Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(15, 0) }, reloaded.GetFixedStartTimes(date));
  }

  [Fact]
  public async Task Fixed_override_empty_times_is_day_off_and_removes_override()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var date = new DateOnly(2026, 8, 5);

    // najpierw ustaw override fixed
    await NewHandler(db, tenantId, employee.Id).Handle(
      new SetScheduleOverrideCommand(employee.Id, date,
        SlotGenerationMode.FixedStartTimes, FixedStartTimes: new[] { new TimeOnly(9, 0) }), ct);
    Assert.Single((await new EmployeeRepository(db).GetByIdAsync(employee.Id))!.Overrides);

    // pusty fixed = dzień wolny → override usunięty
    await NewHandler(db, tenantId, employee.Id).Handle(
      new SetScheduleOverrideCommand(employee.Id, date,
        SlotGenerationMode.FixedStartTimes, FixedStartTimes: System.Array.Empty<TimeOnly>()), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Empty(reloaded!.Overrides);
  }

  // ── helpers ──

  private static SetScheduleOverrideCommandHandler NewHandler(ApplicationDbContext db, Guid tenantId, Guid selfId)
    => new(new EmployeeRepository(db), new AppointmentRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

  private static (ApplicationDbContext db, Guid tenantId, Employee employee) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var employee = new Employee(tenantId, null, "Ala", "Nails", "ala@nails.local");
    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    db.Employees.Add(employee);
    db.SaveChanges();
    return (db, tenantId, employee);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

}
