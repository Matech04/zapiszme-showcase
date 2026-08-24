using App.Application.Appointments.Commands.PlaceAppointment;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Tryb stałych slotów: rezerwacja ONLINE musi trafić w zdefiniowaną godzinę, PANEL może w dowolną.
/// IsAvailable jest zaślepione na true (FakeAvailabilityService) — testujemy WYŁĄCZNIE dodatkową
/// bramkę stałych slotów w <see cref="PlaceAppointmentHandler"/>.
/// </summary>
public sealed class CreateAppointmentFixedModeTests
{
  [Fact]
  public async Task Online_with_off_slot_start_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, monday) = await SetupFixedAsync(ct);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      BuildHandler(db, tenant).Handle(
        new PlaceAppointmentCommand(employee.Id, [service.Id], monday, new TimeOnly(9, 30), null, null,
          CreateAsAwaitingOtp: true, Source: AppointmentSource.Online),
        ct));
  }

  [Fact]
  public async Task Online_with_on_slot_start_succeeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, monday) = await SetupFixedAsync(ct);

    var id = await BuildHandler(db, tenant).Handle(
      new PlaceAppointmentCommand(employee.Id, [service.Id], monday, new TimeOnly(9, 0), null, null,
        CreateAsAwaitingOtp: true, Source: AppointmentSource.Online),
      ct);

    Assert.NotEqual(Guid.Empty, id);
  }

  [Fact]
  public async Task Panel_with_off_slot_start_is_allowed()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service, monday) = await SetupFixedAsync(ct);

    // Panel: 9:30 nie jest zdefiniowanym slotem, ale personel może wpisać dowolną godzinę.
    var id = await BuildHandler(db, tenant).Handle(
      new PlaceAppointmentCommand(employee.Id, [service.Id], monday, new TimeOnly(9, 30), null, null,
        CreateAsBooked: true, Source: AppointmentSource.Panel),
      ct);

    Assert.NotEqual(Guid.Empty, id);
  }

  // ── helpers ──

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

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service, DateOnly monday)> SetupFixedAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Fixed Salon", "fx-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Ala", "Nails", "ala@nails.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Manicure", new Money(120m, "PLN"), 90);

    var monday = NextMonday();
    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    var days = new List<ScheduleDay>
    {
      new(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }, cycleIndex: (int)DayOfWeek.Monday),
    };
    employee.SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(120m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    await db.SaveChangesAsync(ct);

    return (db, tenant, employee, service, monday);
  }

  private static DateOnly NextMonday()
  {
    var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    while (d.DayOfWeek != DayOfWeek.Monday)
    {
      d = d.AddDays(1);
    }
    return d;
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
