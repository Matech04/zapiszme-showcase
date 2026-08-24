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

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Tworzenie wizyty z niestandardowym czasem (override personelu): endTime liczony z override,
/// normalizacja „== standard → null", walidacja wartości niedodatniej. Standard usługi = 30 min.
/// </summary>
public sealed class CreateAppointmentCustomDurationTests
{
  [Fact]
  public async Task Create_with_custom_duration_sets_end_time_from_override()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id);
    var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

    var id = await handler.Handle(new PlaceAppointmentCommand(
      employee.Id, [service.Id], date, new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
      CustomDurationMinutes: 45), ct);

    var appt = await db.Appointments.FindAsync(new object[] { id }, ct);
    Assert.Equal(45, appt!.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(11, 45), appt.EndTime); // 45 zamiast standardowych 30
    Assert.Equal(30, appt.Items.Single().DurationMinutes); // pozycja trzyma standardowy czas usługi
  }

  [Fact]
  public async Task Create_custom_duration_equal_to_standard_is_normalized_to_null()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id);
    var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

    var id = await handler.Handle(new PlaceAppointmentCommand(
      employee.Id, [service.Id], date, new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
      CustomDurationMinutes: 30), ct);

    var appt = await db.Appointments.FindAsync(new object[] { id }, ct);
    Assert.Null(appt!.CustomDurationMinutes); // == standard → czas standardowy
    Assert.Equal(new TimeOnly(11, 30), appt.EndTime);
  }

  [Fact]
  public async Task Create_without_custom_duration_uses_standard()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id);
    var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

    var id = await handler.Handle(new PlaceAppointmentCommand(
      employee.Id, [service.Id], date, new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true), ct);

    var appt = await db.Appointments.FindAsync(new object[] { id }, ct);
    Assert.Null(appt!.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(11, 30), appt.EndTime);
  }

  [Fact]
  public async Task Create_with_non_positive_custom_duration_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id);
    var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new PlaceAppointmentCommand(
        employee.Id, [service.Id], date, new TimeOnly(11, 0),
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
        CustomDurationMinutes: 0), ct));
    Assert.Equal(ErrorCodes.AppointmentInvalidDuration, ex.ErrorCode);
  }

  private static PlaceAppointmentHandler BuildHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new CustomerRepository(db),
      new TenantRepository(db),
      db,
      new FakeCurrentTenantService(tenantId),
      new FakeAvailabilityService(),
      new App.Application.UnitTests.Booking.CapturingPublisher());

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SetupAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Custom Dur Salon", "cdur-" + Guid.NewGuid().ToString("N")[..8]);
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenant.Id));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@cdur.local");
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
    public Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
      => Task.FromResult(true);
    public Task<bool> IsAvailableAsync(Employee employee, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
      => Task.FromResult(true);
    public Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds)
      => Task.FromResult(true);
    public List<TimeOnly> EmployeeAvailableSlotsList(List<TimeRange> schedule, List<TimeRange> appointments, Employee employee, int serviceDuration, int appointmentSlotStepMinutes)
      => new();
    public List<TimeOnly> EmployeeFixedSlotsList(IReadOnlyList<TimeOnly> fixedStartTimes, List<TimeRange> appointments, Employee employee, int serviceDuration)
      => new();
  }
}
