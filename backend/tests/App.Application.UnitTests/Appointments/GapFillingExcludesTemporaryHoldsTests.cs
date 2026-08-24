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
/// APPT-GAP-001 — bug regresji: tymczasowy hold (AwaitingOtp) NIE może wpływać na preferowane
/// sloty w gap-fillingu. W przeciwnym razie squatter trzymający slot 10:00 sztucznie ściąga
/// innych userów do sąsiednich slotów 09:30/10:30.
///
/// Sprawdzane symetrycznie dla obu kierunków: AwaitingOtp NIE wzbudza preferred,
/// Booked wzbudza (sanity-check, że logika gap-fillingu w ogóle żyje).
/// </summary>
public sealed class GapFillingExcludesTemporaryHoldsTests
{
  [Fact]
  public async Task AwaitingOtp_appointment_does_not_create_preferred_neighboring_slots()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedSalonWithGapFillingAsync(ct);

    var date = NextMonday();
    // Tymczasowy hold (squatter) na 10:00–10:30
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.AwaitingOtp);
    await db.SaveChangesAsync(ct);

    var handler = NewHandler(db, tenantId);
    var result = await handler.Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    // Slot 09:30 i 10:30 są dostępne (collision OK), ale NIE są preferowane —
    // bo hold to tymczasowy AwaitingOtp, nie potwierdzona wizyta.
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public async Task Booked_appointment_does_create_preferred_neighboring_slots()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedSalonWithGapFillingAsync(ct);

    var date = NextMonday();
    // Potwierdzona wizyta na 10:00–10:30 — gap-filling powinien preferować sąsiednie sloty.
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var handler = NewHandler(db, tenantId);
    var result = await handler.Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    // Service 30min, step 15min. Adjacent przed = slot kończący się o 10:00, czyli 09:30.
    // Adjacent po = slot zaczynający się o 10:30.
    Assert.Contains(result, r => r.slot == "09:30" && r.isPreferred);
    Assert.Contains(result, r => r.slot == "10:30" && r.isPreferred);
  }

  [Fact]
  public async Task Pending_appointment_creates_preferred_neighboring_slots()
  {
    // Pending w panelu to "klient zarezerwował, salon zatwierdza" — wizyta jest realna,
    // gap-filling powinien sugerować sloty przylegające do niej. Tylko AwaitingOtp
    // (squatter z public-bookingu) jest wykluczany z gap-filling.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedSalonWithGapFillingAsync(ct);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Pending);
    await db.SaveChangesAsync(ct);

    var handler = NewHandler(db, tenantId);
    var result = await handler.Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.Contains(result, r => r.slot == "09:30" && r.isPreferred);
    Assert.Contains(result, r => r.slot == "10:30" && r.isPreferred);
  }

  [Fact]
  public async Task AwaitingOtp_blocks_overlapping_slot_but_not_neighbors_preference()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee, service) = await SeedSalonWithGapFillingAsync(ct);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.AwaitingOtp);
    await db.SaveChangesAsync(ct);

    var handler = NewHandler(db, tenantId);
    var result = await handler.Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    // Collision: slot 10:00 (overlapping) wciąż usunięty, mimo że to tylko hold.
    Assert.DoesNotContain(result, r => r.slot == "10:00");
    Assert.DoesNotContain(result, r => r.slot == "09:45");
    Assert.DoesNotContain(result, r => r.slot == "10:15");
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────

  private static GetAvailableTimeSlotsHandler NewHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

  private static async Task<(ApplicationDbContext db, Guid tenantId, Employee employee, Service service)> SeedSalonWithGapFillingAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("GapFill Salon", "gap-" + Guid.NewGuid().ToString("N")[..8]);
    tenant.Update(
      tenant.Name, tenant.Slug,
      customerVerificationChannel: null,
      appointmentSlotStepMinutes: 15,
      gapFillingSettings: new GapFillingSettings(GapFillingMode.PreferAdjacent, bufferMinutes: 0, lookaheadSlots: 1));

    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Gap", "Worker", "gap@e.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 30);

    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => workRanges);
    employee.SetWeeklySchedule(weekly);
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
    ApplicationDbContext db,
    Guid tenantId,
    Guid employeeId,
    Guid serviceId,
    DateOnly date,
    int startHour,
    int startMinute,
    AppointmentStatus status)
  {
    var appt = new Appointment(
      tenantId, employeeId, serviceId, customerId: null,
      date, new TimeOnly(startHour, startMinute), new TimeOnly(startHour, startMinute).AddMinutes(30),
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
