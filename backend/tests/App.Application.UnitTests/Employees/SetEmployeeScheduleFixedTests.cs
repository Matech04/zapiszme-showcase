using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Commands.SetEmployeeSchedule;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// Zapis grafiku w trybie stałych slotów: utrwala tryb + godziny per dzień, pomija dni bez godzin
/// (= wolne), nie zostawia przedziałów pracy, egzekwuje izolację tenantów.
/// </summary>
public sealed class SetEmployeeScheduleFixedTests
{
  [Fact]
  public async Task Persists_mode_and_fixed_times_per_day()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var dto = new EmployeeScheduleDto(
      ActiveFrom: new DateOnly(2026, 6, 1),
      ActiveTo: new DateOnly(2026, 12, 31),
      NumberOfCycles: 1,
      Days: new List<EmployeeScheduleDayDto>
      {
        new(CycleIndex: 1, WorkRanges: Array.Empty<TimeRangeDto>(), Breaks: Array.Empty<TimeRangeDto>(),
          FixedStartTimes: new[] { new TimeOnly(12, 0), new TimeOnly(9, 0) }),
        new(CycleIndex: 2, WorkRanges: Array.Empty<TimeRangeDto>(), Breaks: Array.Empty<TimeRangeDto>(),
          FixedStartTimes: new[] { new TimeOnly(14, 0) }),
      },
      SlotGenerationMode: SlotGenerationMode.FixedStartTimes);

    await NewHandler(db, tenantId, employee.Id).Handle(new SetEmployeeScheduleCommand(employee.Id, dto), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Equal(SlotGenerationMode.FixedStartTimes, reloaded!.SlotGenerationMode);

    var days = reloaded.Schedules.Single().ScheduleDays.OrderBy(d => d.CycleIndex).ToList();
    Assert.Equal(2, days.Count);
    // Sort + dedupe + brak przedziałów pracy.
    Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0) }, days[0].FixedStartTimes);
    Assert.Equal(new[] { new TimeOnly(14, 0) }, days[1].FixedStartTimes);
    Assert.All(days, d => Assert.Empty(d.WorkRanges));
  }

  [Fact]
  public async Task Day_without_fixed_times_is_treated_as_day_off_and_skipped()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var dto = new EmployeeScheduleDto(
      ActiveFrom: new DateOnly(2026, 6, 1),
      ActiveTo: new DateOnly(2026, 12, 31),
      NumberOfCycles: 1,
      Days: new List<EmployeeScheduleDayDto>
      {
        new(CycleIndex: 1, WorkRanges: Array.Empty<TimeRangeDto>(), Breaks: Array.Empty<TimeRangeDto>(),
          FixedStartTimes: new[] { new TimeOnly(9, 0) }),
        new(CycleIndex: 2, WorkRanges: Array.Empty<TimeRangeDto>(), Breaks: Array.Empty<TimeRangeDto>(),
          FixedStartTimes: Array.Empty<TimeOnly>()),
      },
      SlotGenerationMode: SlotGenerationMode.FixedStartTimes);

    await NewHandler(db, tenantId, employee.Id).Handle(new SetEmployeeScheduleCommand(employee.Id, dto), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    var day = Assert.Single(reloaded!.Schedules.Single().ScheduleDays);
    Assert.Equal(1, day.CycleIndex);
  }

  [Fact]
  public async Task Cross_tenant_throws_TenantViolation()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _, employee) = SetupDb();
    var otherTenant = Guid.NewGuid();

    var dto = new EmployeeScheduleDto(
      ActiveFrom: new DateOnly(2026, 6, 1),
      ActiveTo: new DateOnly(2026, 12, 31),
      NumberOfCycles: 1,
      Days: new List<EmployeeScheduleDayDto>
      {
        new(CycleIndex: 1, WorkRanges: Array.Empty<TimeRangeDto>(), Breaks: Array.Empty<TimeRangeDto>(),
          FixedStartTimes: new[] { new TimeOnly(9, 0) }),
      },
      SlotGenerationMode: SlotGenerationMode.FixedStartTimes);

    await Assert.ThrowsAsync<TenantViolation>(() =>
      NewHandler(db, otherTenant, employee.Id).Handle(new SetEmployeeScheduleCommand(employee.Id, dto), ct));
  }

  // ── helpers ──

  private static SetEmployeeScheduleCommandHandler NewHandler(ApplicationDbContext db, Guid tenantId, Guid selfId)
    => new(new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

  private static (ApplicationDbContext db, Guid tenantId, Employee employee) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var employee = new Employee(tenantId, null, "Ala", "Nails", "ala@nails.local");
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
