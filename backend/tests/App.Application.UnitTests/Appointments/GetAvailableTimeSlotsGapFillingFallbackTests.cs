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
/// Domyślny fallback gap-fillingu dla `GetAvailableTimeSlotsQuery`. Owner panelu salonu
/// powinien ZAWSZE widzieć sugerowane sloty (preferred) — nawet gdy:
///   - świeży tenant nie ma jeszcze zapisanego `GapFillingSettings` (null), albo
///   - settings zostały zapisane z `Mode=Disabled` (semantyka "nie skonfigurowano").
/// `SkipGapFilling=true` (booking self-service) wciąż wyłącza preferred — to osobna ścieżka.
/// `AdjacentOnly` wybrane wprost przez ownerа dalej filtruje sloty.
/// </summary>
public sealed class GetAvailableTimeSlotsGapFillingFallbackTests
{
  [Fact]
  public async Task Null_settings_falls_back_to_PreferAdjacent_and_marks_preferred()
  {
    var ct = TestContext.Current.CancellationToken;
    // gapFillingSettings = null → świeży tenant bez konfiguracji.
    var (db, tenantId, employee, service) = await SeedSalonAsync(ct, gapFillingSettings: null);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.Contains(result, r => r.slot == "09:30" && r.isPreferred);
    Assert.Contains(result, r => r.slot == "10:30" && r.isPreferred);
  }

  [Fact]
  public async Task Disabled_settings_falls_back_to_PreferAdjacent_and_marks_preferred()
  {
    var ct = TestContext.Current.CancellationToken;
    // Owner zapisał `Disabled` — traktujemy jak "domyślnie", bo panel salonu zawsze chce widzieć sugestie.
    var settings = new GapFillingSettings(GapFillingMode.Disabled, bufferMinutes: 0, lookaheadSlots: 1);
    var (db, tenantId, employee, service) = await SeedSalonAsync(ct, gapFillingSettings: settings);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.Contains(result, r => r.slot == "09:30" && r.isPreferred);
    Assert.Contains(result, r => r.slot == "10:30" && r.isPreferred);
  }

  [Fact]
  public async Task SkipGapFilling_returns_all_slots_unmarked_even_if_settings_present()
  {
    var ct = TestContext.Current.CancellationToken;
    var settings = new GapFillingSettings(GapFillingMode.PreferAdjacent, bufferMinutes: 0, lookaheadSlots: 1);
    var (db, tenantId, employee, service) = await SeedSalonAsync(ct, gapFillingSettings: settings);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id], SkipGapFilling: true), ct);

    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public async Task AdjacentOnly_without_enforce_returns_all_slots_with_preferred_marked()
  {
    var ct = TestContext.Current.CancellationToken;
    var settings = new GapFillingSettings(GapFillingMode.AdjacentOnly, bufferMinutes: 0, lookaheadSlots: 1);
    var (db, tenantId, employee, service) = await SeedSalonAsync(ct, gapFillingSettings: settings);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    // Staff panel (brak EnforceStrictGapFilter): wszystkie sloty widoczne, sąsiednie oznaczone.
    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id]), ct);

    Assert.Contains(result, r => r.slot == "09:30" && r.isPreferred);
    Assert.Contains(result, r => r.slot == "10:30" && r.isPreferred);
    Assert.Contains(result, r => !r.isPreferred);
  }

  [Fact]
  public async Task AdjacentOnly_with_EnforceStrictGapFilter_filters_to_preferred_only()
  {
    var ct = TestContext.Current.CancellationToken;
    var settings = new GapFillingSettings(GapFillingMode.AdjacentOnly, bufferMinutes: 0, lookaheadSlots: 1);
    var (db, tenantId, employee, service) = await SeedSalonAsync(ct, gapFillingSettings: settings);

    var date = NextMonday();
    SeedAppointment(db, tenantId, employee.Id, service.Id, date, 10, 00, AppointmentStatus.Booked);
    await db.SaveChangesAsync(ct);

    // Booking self-service (EnforceStrictGapFilter=true): tylko sąsiednie sloty.
    var result = await NewHandler(db, tenantId)
      .Handle(new GetAvailableTimeSlotsQuery(date, employee.Id, [service.Id], EnforceStrictGapFilter: true), ct);

    Assert.All(result, r => Assert.True(r.isPreferred));
    Assert.Contains(result, r => r.slot == "09:30");
    Assert.Contains(result, r => r.slot == "10:30");
  }

  // ── helpers ──────────────────────────────────────────────────────────────────────

  private static GetAvailableTimeSlotsHandler NewHandler(ApplicationDbContext db, Guid tenantId)
    => new(
      db,
      new EmployeeRepository(db),
      new FakeCurrentTenantService(tenantId),
      new AppointmentService(new AppointmentRepository(db)));

  private static async Task<(ApplicationDbContext db, Guid tenantId, Employee employee, Service service)> SeedSalonAsync(
    CancellationToken ct,
    GapFillingSettings? gapFillingSettings)
  {
    var tenant = new Tenant("Fallback Salon", "fb-" + Guid.NewGuid().ToString("N")[..8]);
    if (gapFillingSettings != null)
    {
      // Tenant.Update zapisuje gap-filling tylko gdy nie-null — czyli scenariusz "Disabled
      // świadomie zapisany" wymaga jawnego przekazania settings z Mode=Disabled.
      tenant.Update(
        tenant.Name, tenant.Slug,
        customerVerificationChannel: null,
        appointmentSlotStepMinutes: 15,
        gapFillingSettings: gapFillingSettings);
    }
    else
    {
      // Świeży tenant: nigdy nie wywołał Update z gap-filling → kolumny w DB są null.
      tenant.Update(
        tenant.Name, tenant.Slug,
        customerVerificationChannel: null,
        appointmentSlotStepMinutes: 15);
    }

    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Gap", "Worker", "fb@e.local");
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
