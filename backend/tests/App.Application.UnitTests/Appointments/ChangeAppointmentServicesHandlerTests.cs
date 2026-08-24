using App.Application.Appointments.Commands.ChangeAppointmentServices;
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
/// ChangeAppointmentServicesHandler — podmiana usługi bez zmiany terminu: happy-path (przelicza
/// czas/cenę/usługę główną, zachowuje datę/godzinę/pracownika), kolizja, TenantViolation,
/// status terminalny.
/// </summary>
public sealed class ChangeAppointmentServicesHandlerTests
{
  [Fact]
  public async Task Change_recomputes_time_price_and_primary_service_keeping_term()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, _, _, longService, appointment) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id, available: true);

    var resultId = await handler.Handle(
      new ChangeAppointmentServicesCommand(appointment.Id, [longService.Id]),
      ct);

    Assert.Equal(appointment.Id, resultId);
    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    // Termin (data + godzina rozpoczęcia) i pracownik bez zmian.
    Assert.Equal(appointment.Date, reloaded!.Date);
    Assert.Equal(appointment.StartTime, reloaded.StartTime);
    Assert.Equal(appointment.EmployeeId, reloaded.EmployeeId);
    // Usługa główna i przeliczony czas/cena z nowego (dłuższego) zabiegu.
    Assert.Equal(longService.Id, reloaded.ServiceId);
    Assert.Equal(appointment.StartTime.AddMinutes(60), reloaded.EndTime);
    Assert.Equal(150m, reloaded.TotalPrice.Amount);
    Assert.Single(reloaded.Items);
  }

  [Fact]
  public async Task Change_throws_NotFound_for_unknown_appointment_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, _, _, longService, _) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id, available: true);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ChangeAppointmentServicesCommand(Guid.NewGuid(), [longService.Id]), ct));
  }

  [Fact]
  public async Task Change_does_not_leak_appointment_from_other_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _, _, _, longService, appointment) = await SetupAsync(ct);

    // Inny tenant nie może mutować cudzej wizyty — defensywny check TenantId rzuca TenantViolation
    // (na realnym Postgresie dodatkowo zadziałałby globalny query filter). Brak wycieku/mutacji.
    var handler = BuildHandler(db, Guid.NewGuid(), available: true);

    await Assert.ThrowsAsync<TenantViolation>(() =>
      handler.Handle(new ChangeAppointmentServicesCommand(appointment.Id, [longService.Id]), ct));
  }

  [Fact]
  public async Task Change_throws_AppointmentSlotUnavailable_when_new_service_does_not_fit()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, _, _, longService, appointment) = await SetupAsync(ct);

    var handler = BuildHandler(db, tenant.Id, available: false);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      handler.Handle(new ChangeAppointmentServicesCommand(appointment.Id, [longService.Id]), ct));
  }

  [Fact]
  public async Task Change_throws_for_canceled_appointment()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, _, _, longService, appointment) = await SetupAsync(ct, status: AppointmentStatus.Canceled);

    var handler = BuildHandler(db, tenant.Id, available: true);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new ChangeAppointmentServicesCommand(appointment.Id, [longService.Id]), ct));
    Assert.Equal(ErrorCodes.AppointmentServicesChangeInvalidStatus, ex.ErrorCode);
  }

  private static ChangeAppointmentServicesHandler BuildHandler(ApplicationDbContext db, Guid tenantId, bool available)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantId),
      new FakeAvailabilityService(always: available));

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service, Service longService, Appointment appointment)> SetupAsync(
    CancellationToken ct,
    AppointmentStatus? status = null)
  {
    var tenant = new Tenant("Change Salon", "change-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@change.local");
    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    employee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));

    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));

    // Drugi (dłuższy/droższy) zabieg, na który będziemy podmieniać — przypisany pracownikowi.
    var longService = new Service(tenant.Id, category.Id, vat.Id, "Koloryzacja", new Money(150m, "PLN"), 60);
    employee.AssignService(tenant.Id, longService.Id, longService.DurationInMinutes, new Money(150m, "PLN"));

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appointment = new Appointment(
      tenant.Id, employee.Id, service.Id, null,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      status ?? AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Services.Add(longService);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    return (db, tenant, employee, service, longService, appointment);
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
