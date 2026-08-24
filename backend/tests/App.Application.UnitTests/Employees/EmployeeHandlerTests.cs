using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Commands.AddEmployeeLeave;
using App.Application.Employees.Commands.AssignService;
using App.Application.Employees.Commands.DeleteEmployee;
using App.Application.Employees.Commands.SetEmployeeSchedule;
using App.Application.Employees.Commands.SetScheduleOverride;
using App.Application.Employees.Commands.UnassignService;
using App.Application.Employees.Commands.UpdateEmployeeService;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// APP-EMP — testy handlerów Employee: services (EMP-004), schedule (EMP-007),
/// schedule override (EMP-008), leave (EMP-009), delete (EMP-003).
/// </summary>
public sealed class EmployeeHandlerTests
{
  // EMP-004: AssignService + UnassignService handlers
  [Fact]
  public async Task AssignService_persists_service_to_employee()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new AssignServiceCommandHandler(
      new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var serviceId = Guid.NewGuid();
    await handler.Handle(new AssignServiceCommand(employee.Id, serviceId, 45, 100m, "PLN"), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Contains(reloaded!.Services, s => s.ServiceId == serviceId && s.CustomDuration == 45);
  }

  // EMP-004: przypisanie BEZ custom wartości dziedziczy z katalogu (null = brak override).
  [Fact]
  public async Task AssignService_without_custom_values_inherits_from_catalog()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new AssignServiceCommandHandler(
      new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var serviceId = Guid.NewGuid();
    await handler.Handle(new AssignServiceCommand(employee.Id, serviceId), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    var link = reloaded!.Services.Single(s => s.ServiceId == serviceId);
    Assert.Null(link.CustomDuration);
    Assert.Null(link.CustomPrice);
  }

  // EMP-004: wyczyszczenie override (null) wraca do dziedziczenia z katalogu.
  [Fact]
  public async Task UpdateEmployeeService_with_null_values_clears_override_to_inherit()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var serviceId = Guid.NewGuid();
    employee.AssignService(tenantId, serviceId, 30, new Money(80m, "PLN"));
    db.SaveChanges();

    var handler = new UpdateEmployeeServiceCommandHandler(
      new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    await handler.Handle(new UpdateEmployeeServiceCommand(employee.Id, serviceId), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    var link = reloaded!.Services.Single(s => s.ServiceId == serviceId);
    Assert.Null(link.CustomDuration);
    Assert.Null(link.CustomPrice);
  }

  [Fact]
  public async Task UnassignService_removes_service_from_employee()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var serviceId = Guid.NewGuid();
    employee.AssignService(tenantId, serviceId, 30, new Money(80m, "PLN"));
    db.SaveChanges();

    var handler = new UnassignServiceCommandHandler(
      new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    await handler.Handle(new UnassignServiceCommand(employee.Id, serviceId), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.DoesNotContain(reloaded!.Services, s => s.ServiceId == serviceId);
  }

  // EMP-007: SetEmployeeSchedule — add new (Id is null)
  [Fact]
  public async Task SetEmployeeSchedule_adds_new_schedule_when_Id_is_null()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new SetEmployeeScheduleCommandHandler(
      new EmployeeRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var dto = new EmployeeScheduleDto(
      ActiveFrom: new DateOnly(2026, 6, 1),
      ActiveTo: new DateOnly(2026, 12, 31),
      NumberOfCycles: 1,
      Days: new List<EmployeeScheduleDayDto>
      {
        new(CycleIndex: 1,
          WorkRanges: new List<TimeRangeDto> { new(new TimeOnly(8, 0), new TimeOnly(16, 0)) },
          Breaks: new List<TimeRangeDto>()),
      });

    await handler.Handle(new SetEmployeeScheduleCommand(employee.Id, dto), ct);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Single(reloaded!.Schedules);
  }

  // EMP-008: SetScheduleOverride happy path
  [Fact]
  public async Task SetScheduleOverride_stores_new_working_ranges()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new SetScheduleOverrideCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    var date = new DateOnly(2026, 7, 15);
    var result = await handler.Handle(
      new SetScheduleOverrideCommand(employee.Id, date,
        WorkRanges: new[] { new TimeRangeDto(new TimeOnly(10, 0), new TimeOnly(14, 0)) },
        Breaks: null),
      ct);

    Assert.NotNull(result);
    Assert.Empty(result.AppointmentsOutsideSchedule);

    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Single(reloaded!.Overrides);
    Assert.Equal(date, reloaded.Overrides.First().Date);
  }

  // EMP-008: appointments outside new work range are returned (never block)
  [Fact]
  public async Task SetScheduleOverride_returns_appointments_outside_new_work_range()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var date = new DateOnly(2026, 7, 21);
    db.Appointments.Add(new Appointment(
      tenantId, employee.Id, Guid.NewGuid(), null,
      date, new TimeOnly(8, 0), new TimeOnly(8, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    db.SaveChanges();

    var handler = new SetScheduleOverrideCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(
      new SetScheduleOverrideCommand(employee.Id, date,
        WorkRanges: new[] { new TimeRangeDto(new TimeOnly(12, 0), new TimeOnly(16, 0)) },
        Breaks: null),
      ct);

    Assert.Single(result.AppointmentsOutsideSchedule);
    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Single(reloaded!.Overrides);
  }

  // EMP-008: day-off override succeeds even when appointments exist; returns them as outside
  [Fact]
  public async Task SetScheduleOverride_day_off_returns_existing_appointments_and_proceeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    var date = new DateOnly(2026, 7, 22);
    db.Appointments.Add(new Appointment(
      tenantId, employee.Id, Guid.NewGuid(), null,
      date, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    db.SaveChanges();

    var handler = new SetScheduleOverrideCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(
      new SetScheduleOverrideCommand(employee.Id, date, WorkRanges: null, Breaks: null),
      ct);

    Assert.Single(result.AppointmentsOutsideSchedule);
  }

  // EMP-009: AddLeave happy
  [Fact]
  public async Task AddLeave_stores_leave_correctly()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new AddEmployeeLeaveCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(
      new AddEmployeeLeaveCommand(employee.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)),
      ct);

    Assert.NotNull(result);
    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Single(reloaded!.Leaves);
  }

  // EMP-009 Negative: InvalidLeaveDates
  [Fact]
  public async Task AddLeave_throws_InvalidLeaveDates_when_start_after_end()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new AddEmployeeLeaveCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<InvalidLeaveDatesException>(() =>
      handler.Handle(
        new AddEmployeeLeaveCommand(employee.Id, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 1)),
        ct));
  }

  // EMP-009: AddLeave overlap throws
  [Fact]
  public async Task AddLeave_throws_LeaveOverlap_when_overlaps_existing()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    employee.AddLeave(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
    db.SaveChanges();

    var handler = new AddEmployeeLeaveCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<LeaveOverlapException>(() =>
      handler.Handle(
        new AddEmployeeLeaveCommand(employee.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 10)),
        ct));
  }

  // EMP-009: AddLeave never blocked by appointments; returns list of colliders
  [Fact]
  public async Task AddLeave_returns_colliding_appointments_and_proceeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();
    db.Appointments.Add(new Appointment(
      tenantId, employee.Id, Guid.NewGuid(), null,
      new DateOnly(2026, 8, 3), new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    db.SaveChanges();

    var handler = new AddEmployeeLeaveCommandHandler(
      new EmployeeRepository(db),
      new AppointmentRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(
      new AddEmployeeLeaveCommand(employee.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)),
      ct);

    Assert.Single(result.AppointmentsInLeave);
    var reloaded = await new EmployeeRepository(db).GetByIdAsync(employee.Id);
    Assert.Single(reloaded!.Leaves);
  }

  // EMP-003: DeleteEmployee handler — unknown id throws NotFound
  [Fact]
  public async Task DeleteEmployee_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _) = SetupDb();

    var handler = new DeleteEmployeeCommandHandler(
      new EmployeeRepository(db),
      new FakeDeletionService(),
      db,
      new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteEmployeeCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task DeleteEmployee_soft_deletes_employee()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = SetupDb();

    var handler = new DeleteEmployeeCommandHandler(
      new EmployeeRepository(db),
      new FakeDeletionService(),
      db,
      new FakeCurrentTenantService(tenantId));

    await handler.Handle(new DeleteEmployeeCommand(employee.Id), ct);

    Assert.False(employee.IsActive);
  }

  private static (ApplicationDbContext db, Guid tenantId, Employee employee) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var employee = new Employee(tenantId, null, "Test", "Worker", "test@e.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return (db, tenantId, employee);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }


  private sealed class FakeDeletionService : IDeletionService
  {
    public Task DeleteAsync<TEntity>(TEntity entity, CancellationToken ct = default)
      where TEntity : Entity, ISoftDelete, ITenantEntity
    {
      entity.Deactivate();
      return Task.CompletedTask;
    }
  }
}
