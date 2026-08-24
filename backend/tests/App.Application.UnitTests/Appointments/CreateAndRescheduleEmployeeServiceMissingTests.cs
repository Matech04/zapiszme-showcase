using App.Application.Appointments.Commands.PlaceAppointment;
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
/// APP-APPT-026 / APP-APPT-027 — APPT-014: handlerowe testy jednostkowe (bez HTTP)
/// dla CreateAppointment i RescheduleAppointment rzucających <see cref="EmployeeServiceMissingException"/>
/// kiedy pracownik nie ma przypisanej żądanej usługi.
/// </summary>
public sealed class CreateAndRescheduleEmployeeServiceMissingTests
{
  [Fact]
  public async Task CreateAppointment_throws_EmployeeServiceMissing_when_employee_has_no_service_assigned()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(assignServiceToEmployee: false, ct);

    var handler = new PlaceAppointmentHandler(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new CustomerRepository(db),
      new TenantRepository(db),
      db, new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(always: true),
      new App.Application.UnitTests.Booking.CapturingPublisher());

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

    await Assert.ThrowsAsync<EmployeeServiceMissingException>(() =>
      handler.Handle(
        new PlaceAppointmentCommand(employee.Id, [service.Id], futureDate, new TimeOnly(10, 0), null, null, CreateAsBooked: true),
        ct));
  }

  [Fact]
  public async Task RescheduleAppointment_throws_EmployeeServiceMissing_when_new_employee_lacks_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, originalEmployee, service) = await SetupAsync(assignServiceToEmployee: true, ct);

    var otherEmployee = new Employee(tenant.Id, null, "Bez", "Usług", "no-svc@test.local");
    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    otherEmployee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));
    db.Employees.Add(otherEmployee);

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appointment = new Appointment(
      tenant.Id, originalEmployee.Id, service.Id, null,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    var handler = new ApplyRescheduleHandler(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new TenantRepository(db),
      db, new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(always: true),
      new App.Application.UnitTests.Booking.CapturingPublisher(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplyRescheduleHandler>.Instance);

    await Assert.ThrowsAsync<EmployeeServiceMissingException>(() =>
      handler.Handle(
        new ApplyRescheduleCommand(appointment.Id, otherEmployee.Id, [service.Id], futureDate.AddDays(1), new TimeOnly(11, 0)),
        ct));
  }

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SetupAsync(
    bool assignServiceToEmployee,
    CancellationToken ct)
  {
    var tenant = new Tenant("Test Salon", "test-mismatch-" + Guid.NewGuid().ToString("N")[..8]);
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

    if (assignServiceToEmployee)
    {
      employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));
    }

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
