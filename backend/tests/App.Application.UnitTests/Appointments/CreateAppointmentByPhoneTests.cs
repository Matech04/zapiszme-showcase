using App.Application.Appointments.Commands.PlaceAppointment;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Zapis wizyty w panelu, gdy pracownik podał SAM numer telefonu (bez wskazania klienta z listy).
/// Regresja: wcześniej <c>CustomerPhone</c> w komendzie był value-objectem <c>PhoneNumber</c>
/// (NSwag eksportował obiekt), a panel wysyłał goły string → 400. Dodatkowo martwy short-circuit
/// w <c>ResolveCustomerId</c> gubił numer. Tu pilnujemy semantyki: numer → istniejący klient albo
/// nowy klient-szkielet (Source = Manual), a numer nigdy nie ginie.
/// </summary>
public sealed class CreateAppointmentByPhoneTests
{
  [Fact]
  public async Task PhoneOnly_with_no_existing_customer_creates_manual_stub_and_links_it()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant);

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

    // Numer w formie z formularza (spacje) — handler musi go znormalizować do E.164.
    var id = await handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, new TimeOnly(11, 0),
        CustomerId: null, CustomerPhone: "+48 600 700 800", CreateAsBooked: true),
      ct);

    var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id, ct);
    Assert.NotNull(appointment.CustomerId);

    var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == appointment.CustomerId, ct);
    Assert.Equal("+48600700800", customer.PhoneNumber!.Value);
    Assert.Equal(CustomerSource.Manual, customer.Source);
    Assert.Equal(string.Empty, customer.FirstName);
    Assert.Equal(string.Empty, customer.LastName);
  }

  [Fact]
  public async Task PhoneOnly_with_existing_customer_links_existing_without_duplicating()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);

    var existing = new Customer(tenant.Id, "Ewa", "Klient", "", new PhoneNumber("+48600700800"), "");
    db.Customers.Add(existing);
    await db.SaveChangesAsync(ct);

    var handler = BuildHandler(db, tenant);
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

    var id = await handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, new TimeOnly(11, 0),
        CustomerId: null, CustomerPhone: "+48 600 700 800", CreateAsBooked: true),
      ct);

    var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id, ct);
    Assert.Equal(existing.Id, appointment.CustomerId);

    // Nie powstał duplikat — wciąż dokładnie jeden klient z tym numerem.
    var count = await db.Customers.AsNoTracking().CountAsync(c => c.PhoneNumberSearch == "48600700800", ct);
    Assert.Equal(1, count);
  }

  [Fact]
  public void Validator_rejects_invalid_phone_string()
  {
    var validator = new App.Application.Common.Validation.PlaceAppointmentCommandValidator();
    var cmd = new PlaceAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
      new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: "not-a-phone", CreateAsBooked: true);

    Assert.False(validator.Validate(cmd).IsValid);
  }

  [Fact]
  public void Validator_accepts_valid_phone_string()
  {
    var validator = new App.Application.Common.Validation.PlaceAppointmentCommandValidator();
    var cmd = new PlaceAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
      new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: "+48 600 700 800", CreateAsBooked: true);

    Assert.True(validator.Validate(cmd).IsValid);
  }

  [Fact]
  public void Validator_accepts_null_phone()
  {
    var validator = new App.Application.Common.Validation.PlaceAppointmentCommandValidator();
    var cmd = new PlaceAppointmentCommand(
      Guid.NewGuid(), [Guid.NewGuid()],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
      new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true);

    Assert.True(validator.Validate(cmd).IsValid);
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
    var tenant = new Tenant("Test Salon", "test-phone-" + Guid.NewGuid().ToString("N")[..8]);
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenant.Id));

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
