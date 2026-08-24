using App.Application.Appointments.Commands.ApplyReschedule;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// APP-APPT (APPT-012) — ApplyRescheduleHandler: happy-path, NotFound, TenantViolation.
/// </summary>
public sealed class ApplyRescheduleHandlerTests
{
  [Fact]
  public async Task Reschedule_updates_appointment_time_employee_service_and_price()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var newDate = appointment.Date.AddDays(1);
    var newStartTime = new TimeOnly(12, 0);

    var resultId = await handler.Handle(
      new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], newDate, newStartTime),
      ct);

    Assert.Equal(appointment.Id, resultId);
    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(newDate, reloaded!.Date);
    Assert.Equal(newStartTime, reloaded.StartTime);
    Assert.Equal(employee.Id, reloaded.EmployeeId);
    Assert.Equal(service.Id, reloaded.ServiceId);
  }

  [Fact]
  public async Task Reschedule_preserves_existing_custom_duration_when_request_omits_it()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    // Personel wcześniej ustawił niestandardowy czas 45 min (standard usługi = 30).
    appointment.SetCustomDuration(45);
    await db.SaveChangesAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var newStart = new TimeOnly(9, 0);

    // Przełożenie terminu BEZ podania czasu → override 45 zachowany.
    await handler.Handle(
      new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date, newStart),
      ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(45, reloaded!.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(9, 45), reloaded.EndTime);
  }

  [Fact]
  public async Task Reschedule_with_explicit_custom_duration_overrides()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id);
    var newStart = new TimeOnly(9, 0);

    await handler.Handle(
      new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date, newStart, CustomDurationMinutes: 20),
      ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(20, reloaded!.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(9, 20), reloaded.EndTime);
  }

  [Fact]
  public async Task Reschedule_throws_NotFound_for_unknown_appointment_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, _) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(
        new ApplyRescheduleCommand(Guid.NewGuid(), employee.Id, [service.Id], DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), new TimeOnly(10, 0)),
        ct));
  }

  [Fact]
  public async Task Reschedule_throws_TenantViolation_for_appointment_from_other_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    var otherTenantId = Guid.NewGuid();
    var handler = BuildHandler(db, otherTenantId);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(
        new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date, appointment.StartTime),
        ct));
  }

  [Fact]
  public async Task Reschedule_throws_AppointmentSlotUnavailable_when_service_says_unavailable()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    var handler = new ApplyRescheduleHandler(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new TenantRepository(db),
      db, new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(always: false),
      new App.Application.UnitTests.Booking.CapturingPublisher(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplyRescheduleHandler>.Instance);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      handler.Handle(
        new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date.AddDays(2), new TimeOnly(11, 0)),
        ct));
  }

  [Fact]
  public async Task Reschedule_offSchedule_to_outside_working_hours_throws_without_flag()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    // Realny AppointmentService — grafik 8:00–20:00, 21:00 wypada poza godzinami pracy.
    var handler = BuildRealHandler(db, tenant.Id);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      handler.Handle(
        new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date, new TimeOnly(21, 0)),
        ct));
  }

  [Fact]
  public async Task Reschedule_offSchedule_to_outside_working_hours_succeeds_with_flag()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, appointment) = await SetupAsync(ct);

    var handler = BuildRealHandler(db, tenant.Id);
    var newStart = new TimeOnly(21, 0);

    var resultId = await handler.Handle(
      new ApplyRescheduleCommand(appointment.Id, employee.Id, [service.Id], appointment.Date, newStart, IgnoreSchedule: true),
      ct);

    Assert.Equal(appointment.Id, resultId);
    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(newStart, reloaded!.StartTime);
  }

  private static ApplyRescheduleHandler BuildHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new TenantRepository(db),
      db,
      new FakeCurrentTenantService(tenantId),
      new FakeAvailabilityService(always: true),
      new App.Application.UnitTests.Booking.CapturingPublisher(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplyRescheduleHandler>.Instance);

  // Handler z PRAWDZIWYM AppointmentService — do testów reguły „poza grafikiem".
  private static ApplyRescheduleHandler BuildRealHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new TenantRepository(db),
      db,
      new FakeCurrentTenantService(tenantId),
      new App.Domain.Services.AppointmentService(new AppointmentRepository(db)),
      new App.Application.UnitTests.Booking.CapturingPublisher(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplyRescheduleHandler>.Instance);

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service, Appointment appointment)> SetupAsync(CancellationToken ct)
  {
    var tenant = new Tenant("Reschedule Salon", "reschedule-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@reschedule.local");
    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    employee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));

    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appointment = new Appointment(
      tenant.Id, employee.Id, service.Id, null,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    return (db, tenant, employee, service, appointment);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class FakeAvailabilityService : IAppointmentService
  {
    private readonly bool _always;
    public FakeAvailabilityService(bool always) => _always = always;
    public Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
      => Task.FromResult(_always);
    public Task<bool> IsAvailableAsync(Employee employee, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
      => Task.FromResult(_always);
    public Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds)
      => Task.FromResult(_always);
    public List<TimeOnly> EmployeeAvailableSlotsList(List<TimeRange> schedule, List<TimeRange> appointments, Employee employee, int serviceDuration, int appointmentSlotStepMinutes)
      => new();
    public List<TimeOnly> EmployeeFixedSlotsList(IReadOnlyList<TimeOnly> fixedStartTimes, List<TimeRange> appointments, Employee employee, int serviceDuration)
      => new();
  }
}
