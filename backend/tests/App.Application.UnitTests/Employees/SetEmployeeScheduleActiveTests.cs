using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.UnitTests.Support;
using App.Application.Employees.Commands.SetEmployeeScheduleActive;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// EMP-018 — włączanie/wyłączanie grafiku powtarzalnego: utrwala flagę, blokuje włączenie
/// kolidującego z aktywnym grafikiem, egzekwuje izolację tenantów.
/// </summary>
public sealed class SetEmployeeScheduleActiveTests
{
  [Fact]
  public async Task Activates_inactive_schedule()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var scheduleId = employee.AddSchedule(JuneRange(), 1, OneDay(1), isActive: false);
    db.SaveChanges();

    await NewHandler(db, tenantId, employee.Id)
      .Handle(new SetEmployeeScheduleActiveCommand(employee.Id, scheduleId, IsActive: true), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.True(reloaded!.Schedules.Single(s => s.Id == scheduleId).IsActive);
  }

  [Fact]
  public async Task Deactivates_active_schedule()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var scheduleId = employee.AddSchedule(JuneRange(), 1, OneDay(1));
    db.SaveChanges();

    await NewHandler(db, tenantId, employee.Id)
      .Handle(new SetEmployeeScheduleActiveCommand(employee.Id, scheduleId, IsActive: false), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.False(reloaded!.Schedules.Single(s => s.Id == scheduleId).IsActive);
  }

  [Fact]
  public async Task Activating_overlapping_active_throws_SchedulesCollision()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    employee.AddSchedule(JuneRange(), 1, OneDay(1)); // aktywny
    var draftId = employee.AddSchedule(
      new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)), 1, OneDay(1), isActive: false);
    db.SaveChanges();

    await Assert.ThrowsAsync<SchedulesCollisionException>(() =>
      NewHandler(db, tenantId, employee.Id)
        .Handle(new SetEmployeeScheduleActiveCommand(employee.Id, draftId, IsActive: true), ct));
  }

  [Fact]
  public async Task Unknown_schedule_throws_NotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    await Assert.ThrowsAsync<NotFoundException>(() =>
      NewHandler(db, tenantId, employee.Id)
        .Handle(new SetEmployeeScheduleActiveCommand(employee.Id, Guid.NewGuid(), IsActive: false), ct));
  }

  [Fact]
  public async Task Cross_tenant_throws_TenantViolation()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _, employee) = SetupDb();
    var scheduleId = employee.AddSchedule(JuneRange(), 1, OneDay(1), isActive: false);
    db.SaveChanges();
    var otherTenant = Guid.NewGuid();

    await Assert.ThrowsAsync<TenantViolation>(() =>
      NewHandler(db, otherTenant, employee.Id)
        .Handle(new SetEmployeeScheduleActiveCommand(employee.Id, scheduleId, IsActive: true), ct));
  }

  // ── helpers ──

  private static DateRange JuneRange() => new(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

  private static ScheduleDay[] OneDay(int cycleIndex)
  {
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    return new[] { new ScheduleDay(workRanges, breaks: null, cycleIndex) };
  }

  private static SetEmployeeScheduleActiveCommandHandler NewHandler(ApplicationDbContext db, Guid tenantId, Guid selfId)
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
