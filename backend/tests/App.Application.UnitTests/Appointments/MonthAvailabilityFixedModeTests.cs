using App.Application.Appointments.Queries.GetAppointmentMonthAvailability;
using App.Application.Booking.BookingAppointments.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Services;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Dostępność miesięczna (panel i publiczna rezerwacja) w trybie stałych slotów liczy wpisane godziny
/// per dzień (minus kolizje), bez gap-fillingu.
/// </summary>
public sealed class MonthAvailabilityFixedModeTests
{
  [Fact]
  public async Task Staff_month_counts_fixed_slots_per_day()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedFixedSalonAsync(ct);
    var monday = NextMonday();

    var handler = new GetAppointmentMonthAvailabilityQueryHandler(
      db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetAppointmentMonthAvailabilityQuery(monday.Year, monday.Month, employee.Id, [service.Id]), ct);

    Assert.Equal(3, result.Single(r => r.date == monday).availableCount);
  }

  [Fact]
  public async Task Booking_month_counts_fixed_slots_minus_collision()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedFixedSalonAsync(ct);
    var monday = NextMonday();
    // Booked 12:00–13:00 → slot 12:00 wypada, zostają 2.
    var start = new TimeOnly(12, 0);
    db.Appointments.Add(new Appointment(
      tenantId, employee.Id, service.Id, null, monday, start, start.AddMinutes(60),
      AppointmentStatus.Booked, new Money(50m, "PLN"), "", lease: null, source: AppointmentSource.Online));
    await db.SaveChangesAsync(ct);

    var handler = new GetBookingMonthAvailabilityQueryHandler(
      db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

    var result = await handler.Handle(
      new GetBookingMonthAvailabilityQuery(monday.Year, monday.Month, employee.Id, [service.Id]), ct);

    Assert.Equal(2, result.days.Single(r => r.date == monday).availableCount);
  }

  // ── helpers ──

  private static async Task<(ApplicationDbContext db, Guid tenantId, Employee employee, Service service)> SeedFixedSalonAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("Fixed Salon", "fx-" + Guid.NewGuid().ToString("N")[..8]);
    tenant.Update(tenant.Name, tenant.Slug, customerVerificationChannel: null, appointmentSlotStepMinutes: 15);

    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Ala", "Nails", "ala@nails.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 60);

    employee.SetSlotGenerationMode(SlotGenerationMode.FixedStartTimes);
    var days = Enum.GetValues<DayOfWeek>()
      .Select(d => new ScheduleDay(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(15, 0) }, cycleIndex: (int)d))
      .ToList();
    employee.SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(50m, "PLN"));

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    await db.SaveChangesAsync(ct);

    return (db, tenantId, employee, service);
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
}
