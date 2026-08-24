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
/// B3 (preflight MEDIUM): gdy collision-check przepuści slot (wygasły hold ignorowany), ale zapis
/// uderzy w partial-unique-index (Postgres SqlState 23505), handler mapuje to na semantyczne
/// <see cref="AppointmentSlotUnavailableException"/> (errorCode appointment.slot_unavailable),
/// zamiast pozwolić DbUpdateException polecieć jako generyczny persistence.failed. Inne
/// DbUpdateException (np. naruszenie FK) propagują bez zmiany.
///
/// Deterministyczne: InMemory nie egzekwuje unikalnych indeksów, więc wstrzykujemy wyjątek przez
/// fałszywy IUnitOfWork — testujemy WYŁĄCZNIE logikę catch+mapping handlera, nie sam Postgres.
/// </summary>
public sealed class CreateAppointmentSlotConflictTests
{
  [Fact]
  public async Task Unique_violation_23505_maps_to_slot_unavailable()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant, new ThrowingUnitOfWork(new FakeUniqueViolation()));

    var cmd = new PlaceAppointmentCommand(
      employee.Id, [service.Id],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() => handler.Handle(cmd, ct));
  }

  [Fact]
  public async Task Non_unique_db_error_propagates_unchanged()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    // SqlState inny niż 23505 (np. 23503 = foreign_key_violation) → NIE mapujemy, propaguje dalej.
    var handler = BuildHandler(db, tenant, new ThrowingUnitOfWork(new FakeSqlError("23503")));

    var cmd = new PlaceAppointmentCommand(
      employee.Id, [service.Id],
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), new TimeOnly(11, 0),
      CustomerId: null, CustomerPhone: null, CreateAsBooked: true);

    await Assert.ThrowsAsync<DbUpdateException>(() => handler.Handle(cmd, ct));
  }

  private static PlaceAppointmentHandler BuildHandler(ApplicationDbContext db, Tenant tenant, IUnitOfWork uow)
    => new(
      new AppointmentRepository(db),
      new EmployeeRepository(db),
      new ServiceRepository(db),
      new CustomerRepository(db),
      new TenantRepository(db),
      uow,
      new FakeCurrentTenantService(tenant.Id),
      new FakeAvailabilityService(),
      new App.Application.UnitTests.Booking.CapturingPublisher());

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SetupAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Test Salon", "test-slot-" + Guid.NewGuid().ToString("N")[..8]);
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

  // IUnitOfWork, który zawsze rzuca DbUpdateException z podanym inner-exceptionem przy zapisie.
  private sealed class ThrowingUnitOfWork : IUnitOfWork
  {
    private readonly Exception _inner;
    public ThrowingUnitOfWork(Exception inner) => _inner = inner;
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
      => throw new DbUpdateException("simulated", _inner);
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
      => action(ct);
  }

  // Inner z property SqlState="23505" — odwzorowuje Npgsql.PostgresException (unique_violation).
  private sealed class FakeUniqueViolation : Exception
  {
    public string SqlState => "23505";
  }

  private sealed class FakeSqlError : Exception
  {
    public FakeSqlError(string sqlState) => SqlState = sqlState;
    public string SqlState { get; }
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
