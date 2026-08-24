using App.Application.Appointments.Commands.PlaceAppointment;
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
/// CreateAppointment z flagą CreateAsCompleted — staff może zapisać wizytę w przeszłości
/// jako Completed (np. wizyta odbyła się, ale wcześniej nie była w systemie).
/// </summary>
public sealed class CreateCompletedAppointmentTests
{
  [Fact]
  public async Task CreateAsCompleted_with_past_date_persists_appointment_with_Completed_status()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant);

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

    var id = await handler.Handle(
      new PlaceAppointmentCommand(employee.Id, [service.Id], pastDate, new TimeOnly(10, 0), null, null, CreateAsCompleted: true),
      ct);

    var saved = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id, ct);
    Assert.Equal(AppointmentStatus.Completed, saved.Status);
    Assert.Equal(pastDate, saved.Date);
  }

  [Fact]
  public async Task CreateAsCompleted_with_future_date_throws_AppointmentBookingRuleException()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant);

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(
        new PlaceAppointmentCommand(employee.Id, [service.Id], futureDate, new TimeOnly(10, 0), null, null, CreateAsCompleted: true),
        ct));
  }

  [Fact]
  public async Task Create_with_past_date_without_CreateAsCompleted_throws_AppointmentBookingRuleException()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant);

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(
        new PlaceAppointmentCommand(employee.Id, [service.Id], pastDate, new TimeOnly(10, 0), null, null, CreateAsBooked: true),
        ct));
  }

  [Fact]
  public async Task CreateAsCompleted_with_collision_throws_AppointmentSlotUnavailableException()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var handler = new PlaceAppointmentHandler(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new CustomerRepository(db),
      new TenantRepository(db),
      db, new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(always: false),
      new App.Application.UnitTests.Booking.CapturingPublisher());

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      handler.Handle(
        new PlaceAppointmentCommand(employee.Id, [service.Id], pastDate, new TimeOnly(10, 0), null, null, CreateAsCompleted: true),
        ct));
  }

  [Fact]
  public async Task CreateAsBooked_in_panel_publishes_StaffBookedAppointmentEvent()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var publisher = new App.Application.UnitTests.Booking.CapturingPublisher();
    var handler = new PlaceAppointmentHandler(
      new AppointmentRepository(db), new EmployeeRepository(db), new ServiceRepository(db),
      new CustomerRepository(db), new TenantRepository(db), db, new FakeCurrentTenantService(tenant.Id), new FakeAvailabilityService(always: true), publisher);

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var id = await handler.Handle(
      new PlaceAppointmentCommand(employee.Id, [service.Id], futureDate, new TimeOnly(10, 0), null, null, CreateAsBooked: true),
      ct);

    var ev = Assert.Single(publisher.Published.OfType<App.Application.Notifications.Events.StaffBookedAppointmentEvent>());
    Assert.Equal(id, ev.AppointmentId);
    Assert.Equal(tenant.Id, ev.TenantId);
  }

  [Fact]
  public async Task CreateAsCompleted_does_not_publish_StaffBookedAppointmentEvent()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var publisher = new App.Application.UnitTests.Booking.CapturingPublisher();
    var handler = new PlaceAppointmentHandler(
      new AppointmentRepository(db), new EmployeeRepository(db), new ServiceRepository(db),
      new CustomerRepository(db), new TenantRepository(db), db, new FakeCurrentTenantService(tenant.Id), new FakeAvailabilityService(always: true), publisher);

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
    await handler.Handle(
      new PlaceAppointmentCommand(employee.Id, [service.Id], pastDate, new TimeOnly(10, 0), null, null, CreateAsCompleted: true),
      ct);

    Assert.Empty(publisher.Published.OfType<App.Application.Notifications.Events.StaffBookedAppointmentEvent>());
  }

  [Fact]
  public void Validator_rejects_CreateAsCompleted_combined_with_CreateAsBooked()
  {
    var validator = new App.Application.Common.Validation.PlaceAppointmentCommandValidator();
    var cmd = new PlaceAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
      new TimeOnly(10, 0),
      null, null,
      CreateAsBooked: true,
      CreateAsCompleted: true);

    var result = validator.Validate(cmd);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void Validator_rejects_CreateAsCompleted_combined_with_CreateAsAwaitingOtp()
  {
    var validator = new App.Application.Common.Validation.PlaceAppointmentCommandValidator();
    var cmd = new PlaceAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
      new TimeOnly(10, 0),
      null, null,
      CreateAsAwaitingOtp: true,
      CreateAsCompleted: true);

    var result = validator.Validate(cmd);

    Assert.False(result.IsValid);
  }

  private static PlaceAppointmentHandler BuildHandler(ApplicationDbContext db, Tenant tenant)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new CustomerRepository(db),
      new TenantRepository(db),
      db,
      new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(always: true),
      new App.Application.UnitTests.Booking.CapturingPublisher());

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SetupAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Test Salon", "test-completed-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@test.local");

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    employee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));

    var service = new Service(tenant.Id, category.Id, vat.Id, "Cięcie", new Money(80m, "PLN"), 30);
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
