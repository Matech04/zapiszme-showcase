using App.Application.Appointments.Commands.SetAppointmentDuration;
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
/// SetAppointmentDurationHandler — personel reguluje długość bloku per wizyta: skrócenie/wydłużenie,
/// powrót do standardu (null), normalizacja „== standard → null", kolizja (409), TenantViolation,
/// status terminalny.
/// </summary>
public sealed class SetAppointmentDurationHandlerTests
{
  [Fact]
  public async Task Shortens_appointment_block()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: true);

    await handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 40), ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Equal(40, reloaded!.CustomDurationMinutes);
    Assert.Equal(appointment.StartTime.AddMinutes(40), reloaded.EndTime);
  }

  [Fact]
  public async Task Null_resets_to_standard_duration()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: true);

    await handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 40), ct);
    await handler.Handle(new SetAppointmentDurationCommand(appointment.Id, null), ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Null(reloaded!.CustomDurationMinutes);
    Assert.Equal(appointment.StartTime.AddMinutes(60), reloaded.EndTime); // standard = 60
  }

  [Fact]
  public async Task Value_equal_to_standard_is_normalized_to_null()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: true);

    await handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 60), ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Null(reloaded!.CustomDurationMinutes);
  }

  [Fact]
  public async Task Throws_SlotUnavailable_when_longer_block_collides()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: false);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 180), ct));
  }

  [Fact]
  public async Task Throws_NotFound_for_unknown_appointment()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, _) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: true);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetAppointmentDurationCommand(Guid.NewGuid(), 40), ct));
  }

  [Fact]
  public async Task Does_not_leak_appointment_from_other_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, Guid.NewGuid(), available: true);

    await Assert.ThrowsAsync<TenantViolation>(() =>
      handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 40), ct));
  }

  [Fact]
  public async Task Throws_for_canceled_appointment()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct, status: AppointmentStatus.Canceled);
    var handler = BuildHandler(db, tenant.Id, available: true);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new SetAppointmentDurationCommand(appointment.Id, 40), ct));
    Assert.Equal(ErrorCodes.AppointmentServicesChangeInvalidStatus, ex.ErrorCode);
  }

  // Obrona w głąb: handler wołany wprost omija pipeline walidacji (ValidationBehavior), więc absurdalna
  // wartość trafia do domeny — NormalizeCustomDuration MUSI ją odrzucić, zanim zawinie TimeOnly i rozjedzie
  // EndTime z CustomDurationMinutes. Walidator (czyste 400) sprawdzamy osobno niżej.
  [Theory]
  [InlineData(-1)]
  [InlineData(0)]
  [InlineData(Appointment.MaxCustomDurationMinutes + 1)] // 1441
  [InlineData(5000)]
  [InlineData(int.MaxValue)]
  public async Task Rejects_out_of_range_duration_at_domain_level(int minutes)
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, appointment) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant.Id, available: true);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new SetAppointmentDurationCommand(appointment.Id, minutes), ct));
    Assert.Equal(ErrorCodes.AppointmentInvalidDuration, ex.ErrorCode);

    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.Null(reloaded!.CustomDurationMinutes); // brak korupcji zapisu
  }

  [Theory]
  [InlineData(null, true)]  // reset do standardu
  [InlineData(1, true)]
  [InlineData(90, true)]
  [InlineData(Appointment.MaxCustomDurationMinutes, true)] // 1440 = dokładnie 24h
  [InlineData(0, false)]
  [InlineData(-5, false)]
  [InlineData(Appointment.MaxCustomDurationMinutes + 1, false)] // 1441
  [InlineData(100000, false)]
  public void Validator_enforces_duration_range(int? minutes, bool expectedValid)
  {
    var result = new SetAppointmentDurationCommandValidator()
      .Validate(new SetAppointmentDurationCommand(Guid.NewGuid(), minutes));
    Assert.Equal(expectedValid, result.IsValid);
  }

  [Fact]
  public void Validator_rejects_empty_appointment_id()
  {
    var result = new SetAppointmentDurationCommandValidator()
      .Validate(new SetAppointmentDurationCommand(Guid.Empty, 60));
    Assert.False(result.IsValid);
  }

  private static SetAppointmentDurationHandler BuildHandler(ApplicationDbContext db, Guid tenantId, bool available)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      db,
      new FakeCurrentTenantService(tenantId),
      new FakeAvailabilityService(always: available),
      new PermissiveStaffAccessPolicy());

  private static async Task<(ApplicationDbContext db, Tenant tenant, Appointment appointment)> SetupAsync(
    CancellationToken ct,
    AppointmentStatus? status = null)
  {
    var tenant = new Tenant("Duration Salon", "dur-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@dur.local");
    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    employee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));

    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 60);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    // Wizyta 60 min combo-konstruktorem (jedna pozycja) → Items niesie standardowy czas 60.
    var line = new AppointmentServiceLine(service.Id, 60, new Money(80m, "PLN"));
    var appointment = new Appointment(
      tenant.Id, employee.Id, null,
      futureDate, new TimeOnly(10, 0),
      status ?? AppointmentStatus.Booked,
      string.Empty, null, new[] { line });

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    return (db, tenant, appointment);
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
