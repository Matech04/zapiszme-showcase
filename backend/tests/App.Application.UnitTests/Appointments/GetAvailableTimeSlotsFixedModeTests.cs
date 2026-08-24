using App.Application.Appointments.Queries.GetAvailableTimeSlots;
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
/// Tryb stałych slotów w zapytaniu o dostępność dnia: zwraca dokładnie wpisane godziny (minus kolizje),
/// wszystkie nie-preferred, z POMINIĘCIEM gap-fillingu (nawet AdjacentOnly + EnforceStrictGapFilter).
/// </summary>
public sealed class GetAvailableTimeSlotsFixedModeTests
{
  [Fact]
  public async Task Returns_exactly_fixed_slots_when_no_appointments()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedFixedSalonAsync(ct, gapFillingSettings: null);
    var date = NextMonday();

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.Equal(new[] { "09:00", "12:00", "15:00" }, result.Select(r => r.slot).ToArray());
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public async Task Colliding_fixed_slot_is_excluded()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedFixedSalonAsync(ct, gapFillingSettings: null);
    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 9, 0, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.DoesNotContain(result, r => r.slot == "09:00");
    Assert.Contains(result, r => r.slot == "12:00");
    Assert.Contains(result, r => r.slot == "15:00");
  }

  [Fact]
  public async Task Gap_filling_is_bypassed_even_with_AdjacentOnly_and_strict()
  {
    var ct = TestContext.Current.CancellationToken;
    // AdjacentOnly + strict (publiczna rezerwacja) normalnie ukryłby sloty niesąsiadujące z wizytą.
    var settings = new GapFillingSettings(GapFillingMode.AdjacentOnly, bufferMinutes: 0, lookaheadSlots: 1);
    var (db, tenantId, employee, service) = await SeedFixedSalonAsync(ct, gapFillingSettings: settings);
    var date = NextMonday();
    // Wizyta 12:00–13:00 — w trybie siatki strict zostawiłby tylko sąsiednie; tryb fixed ignoruje to.
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 12, 0, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id], EnforceStrictGapFilter: true), ct);

    // 12:00 wypada przez kolizję; 9:00 i 15:00 (niesąsiadujące) ZOSTAJĄ — dowód pominięcia gap-fillingu.
    Assert.Contains(result, r => r.slot == "09:00");
    Assert.Contains(result, r => r.slot == "15:00");
    Assert.DoesNotContain(result, r => r.slot == "12:00");
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  // ── helpers ──

  private static GetAvailableTimeSlotsHandler NewHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

  private static async Task<(ApplicationDbContext db, Guid tenantId, Employee employee, Service service)> SeedFixedSalonAsync(
    CancellationToken ct,
    GapFillingSettings? gapFillingSettings)
  {
    var tenant = new Tenant("Fixed Salon", "fx-" + Guid.NewGuid().ToString("N")[..8]);
    tenant.Update(
      tenant.Name, tenant.Slug,
      customerVerificationChannel: null,
      appointmentSlotStepMinutes: 15,
      gapFillingSettings: gapFillingSettings);

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

  private static void SeedAppointment(
    ApplicationDbContext db, Guid tenantId, Guid employeeId, Guid serviceId,
    DateOnly date, int startHour, int startMinute, AppointmentStatus status)
  {
    var start = new TimeOnly(startHour, startMinute);
    var appt = new Appointment(
      tenantId, employeeId, serviceId, customerId: null,
      date, start, start.AddMinutes(60),
      status, new Money(50m, "PLN"), "", lease: null, source: AppointmentSource.Online);
    db.Appointments.Add(appt);
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
