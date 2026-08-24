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
/// Zapis wizyty „poza grafikiem" z panelu (personel). Pracownik pracuje 8:00–16:00,
/// a personel próbuje wpisać wizytę o 20:00 (poza godzinami pracy). Reguła:
///  - domyślnie (IgnoreSchedule=false) → poza grafikiem niedostępne (AppointmentSlotUnavailableException),
///  - IgnoreSchedule=true + Source=Panel → zapis przechodzi, blokuje tylko realna kolizja,
///  - IgnoreSchedule=true + Source=Online → flaga zignorowana (online nie może pominąć grafiku).
/// Używamy PRAWDZIWEGO AppointmentService (z realnym repo), więc test pokrywa pełną ścieżkę.
/// </summary>
public sealed class CreateAppointmentOffScheduleTests
{
  private static readonly TimeOnly OffHoursStart = new(20, 0);

  [Fact]
  public async Task OutsideWorkingHours_withoutIgnoreSchedule_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant);
    var futureDate = NextMonday();

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() => handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, OffHoursStart,
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true),
      ct));
  }

  [Fact]
  public async Task OutsideWorkingHours_withIgnoreSchedule_fromPanel_succeeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant);
    var futureDate = NextMonday();

    var id = await handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, OffHoursStart,
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
        Source: AppointmentSource.Panel, IgnoreSchedule: true),
      ct);

    var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id, ct);
    Assert.Equal(OffHoursStart, appointment.StartTime);
    Assert.Equal(AppointmentStatus.Booked, appointment.Status);
  }

  [Fact]
  public async Task OutsideWorkingHours_withIgnoreSchedule_butOnlineSource_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant);
    var futureDate = NextMonday();

    // Online nie może pominąć grafiku — guard w handlerze degraduje flagę do false.
    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() => handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, OffHoursStart,
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
        Source: AppointmentSource.Online, IgnoreSchedule: true),
      ct));
  }

  [Fact]
  public async Task OffSchedule_withCollidingAppointment_throws_evenWithIgnoreSchedule()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, employee, service) = await SetupAsync(ct);
    var handler = BuildHandler(db, tenant);
    var futureDate = NextMonday();

    // Pierwsza wizyta poza grafikiem (20:00–20:30) — przechodzi.
    await handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, OffHoursStart,
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
        Source: AppointmentSource.Panel, IgnoreSchedule: true),
      ct);

    // Druga w tym samym czasie — mimo ignoreSchedule kolizja blokuje.
    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() => handler.Handle(
      new PlaceAppointmentCommand(
        employee.Id, [service.Id], futureDate, OffHoursStart,
        CustomerId: null, CustomerPhone: null, CreateAsBooked: true,
        Source: AppointmentSource.Panel, IgnoreSchedule: true),
      ct));
  }

  private static DateOnly NextMonday()
  {
    var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
    while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
    return d;
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
      new AppointmentService(new AppointmentRepository(db)),
      new App.Application.UnitTests.Booking.CapturingPublisher());

  private static async Task<(ApplicationDbContext db, Tenant tenant, Employee employee, Service service)> SetupAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Test Salon", "test-offsch-" + Guid.NewGuid().ToString("N")[..8]);
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenant.Id));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Anna", "Test", "anna@test.local");

    // Grafik tylko 8:00–16:00 — 20:00 wypada poza godzinami pracy.
    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
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
}
