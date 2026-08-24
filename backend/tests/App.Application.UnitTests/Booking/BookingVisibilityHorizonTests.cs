using App.Application.Appointments.Queries.GetAvailableTimeSlots;
using App.Application.Booking.BookingAppointments.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// Widoczność terminów dla klienta: horyzont rezerwacji + publikacja miesiąca.
/// Granica, której te testy pilnują: reguła obowiązuje TYLKO w publicznym bookingu.
/// Panel musi dalej wpisywać wizyty dowolnie daleko i w zamkniętym miesiącu.
/// </summary>
public sealed class BookingVisibilityHorizonTests
{
  private const int HorizonDays = 120;

  [Fact]
  public async Task Public_booking_returns_no_slots_beyond_horizon()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    var beyond = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(HorizonDays + 14));

    var slots = await HandleAsync(db, tenantId, beyond, employee.Id, service.Id, publicFlow: true, ct);

    Assert.Empty(slots);
  }

  [Fact]
  public async Task Panel_still_returns_slots_beyond_horizon()
  {
    // Sedno rozdziału: ten sam dzień, ta sama baza — panel widzi, klient nie.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    var beyond = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(HorizonDays + 14));

    var slots = await HandleAsync(db, tenantId, beyond, employee.Id, service.Id, publicFlow: false, ct);

    Assert.NotEmpty(slots);
  }

  [Fact]
  public async Task Public_booking_returns_no_slots_in_month_closed_until_future_date()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    // Dzień dobrze wewnątrz horyzontu — gdyby nie publikacja, byłby widoczny.
    var target = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40));
    employee.SetMonthPublication(target.Year, target.Month, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
    await db.SaveChangesAsync(ct);

    var slots = await HandleAsync(db, tenantId, target, employee.Id, service.Id, publicFlow: true, ct);

    Assert.Empty(slots);
  }

  [Fact]
  public async Task Panel_still_returns_slots_in_closed_month()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    var target = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40));
    employee.SetMonthPublication(target.Year, target.Month, null); // zamknięty bezterminowo
    await db.SaveChangesAsync(ct);

    var slots = await HandleAsync(db, tenantId, target, employee.Id, service.Id, publicFlow: false, ct);

    Assert.NotEmpty(slots);
  }

  [Fact]
  public async Task Explicit_publication_opens_a_month_that_lies_beyond_the_horizon()
  {
    // „Otwieramy grudzień już teraz, bo święta" — wiersz miesiąca wygrywa z horyzontem.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    var beyond = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(HorizonDays + 14));
    employee.SetMonthPublication(beyond.Year, beyond.Month, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
    await db.SaveChangesAsync(ct);

    var slots = await HandleAsync(db, tenantId, beyond, employee.Id, service.Id, publicFlow: true, ct);

    Assert.NotEmpty(slots);
  }

  [Fact]
  public async Task Month_availability_reports_closure_with_opening_date()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedAsync(ct);

    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
    var opensOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    employee.SetMonthPublication(target.Year, target.Month, opensOn);
    await db.SaveChangesAsync(ct);

    var handler = new GetBookingMonthAvailabilityQueryHandler(
      db, new EmployeeRepository(db), new FakeTenant(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetBookingMonthAvailabilityQuery(target.Year, target.Month, employee.Id, [service.Id]), ct);

    Assert.True(result.isClosed);
    Assert.Equal(opensOn, result.opensOn);
    // Front musi dostać komplet dni (żeby narysować siatkę), ale wszystkie puste.
    Assert.Equal(DateTime.DaysInMonth(target.Year, target.Month), result.days.Count);
    Assert.All(result.days, d => Assert.Equal(0, d.availableCount));
    // isWorkingDay=false, żeby kalendarz nie pokazał tego jako „wszystko zajęte".
    Assert.All(result.days, d => Assert.False(d.isWorkingDay));
  }

  // ── helpers ──

  private static DateOnly NextMonday(DateOnly from)
  {
    var d = from;
    while (d.DayOfWeek != DayOfWeek.Monday)
    {
      d = d.AddDays(1);
    }
    return d;
  }

  private static async Task<List<App.Application.Appointments.Dtos.AppointmentSlotDto>> HandleAsync(
    ApplicationDbContext db, Guid tenantId, DateOnly date, Guid employeeId, Guid serviceId, bool publicFlow, CancellationToken ct)
  {
    var handler = new GetAvailableTimeSlotsHandler(
      db, new EmployeeRepository(db), new FakeTenant(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    return await handler.Handle(
      new GetAvailableTimeSlotsQuery(date, employeeId, [serviceId], EnforcePublicVisibility: publicFlow), ct);
  }

  private static async Task<(ApplicationDbContext db, Guid tenantId, Employee employee, Service service)> SeedAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Horizon Salon", "hz-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeTenant(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Hz", "Worker", "hz@horizon.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);

    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    // Grafik na każdy dzień tygodnia — testy celują w konkretne daty, nie w układ tygodnia.
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => workRanges);
    employee.SetWeeklySchedule(weekly);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(80m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    await db.SaveChangesAsync(ct);

    return (db, tenantId, employee, service);
  }

  private sealed class FakeTenant : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeTenant(Guid tenantId) => TenantId = tenantId;
  }
}
