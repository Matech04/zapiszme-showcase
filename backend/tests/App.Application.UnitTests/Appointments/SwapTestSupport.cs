using App.Application.Appointments.Commands.SwapAppointments;
using App.Application.Appointments.Queries.PreviewSwapAppointments;
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
using Microsoft.Extensions.Logging.Abstractions;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Wspólny seed dla testów zamiany terminów (komenda + podgląd). Buduje tenant, dwóch
/// pracowników, krótką (30 min) i długą (60 min) usługę oraz prawdziwy AppointmentService nad
/// InMemory EF — żeby testy weryfikowały realną logikę dostępności, a nie atrapę.
/// </summary>
internal sealed record SwapEnv(
  ApplicationDbContext Db, Tenant Tenant, Employee Emp1, Employee Emp2,
  Service Short, Service Long, DateOnly Day);

internal static class SwapTestSupport
{
  public static async Task<SwapEnv> SeedAsync(
    CancellationToken ct,
    TimeOnly? emp2Start = null,
    TimeOnly? emp2End = null,
    bool emp1OffersShort = true)
  {
    var tenant = new Tenant("Swap Salon", "swap-" + Guid.NewGuid().ToString("N")[..8]);
    var db = new ApplicationDbContext(
      new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
      new FakeCurrentTenantService(tenant.Id));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var shortService = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(50m, "PLN"), 30);
    var longService = new Service(tenant.Id, category.Id, vat.Id, "Koloryzacja", new Money(120m, "PLN"), 60);

    var emp1 = BuildEmployee(tenant.Id, "Anna", new TimeOnly(8, 0), new TimeOnly(20, 0));
    emp1.AssignService(tenant.Id, longService.Id, longService.DurationInMinutes, longService.Price);
    if (emp1OffersShort)
    {
      emp1.AssignService(tenant.Id, shortService.Id, shortService.DurationInMinutes, shortService.Price);
    }

    var emp2 = BuildEmployee(tenant.Id, "Bartek", emp2Start ?? new TimeOnly(8, 0), emp2End ?? new TimeOnly(20, 0));
    emp2.AssignService(tenant.Id, shortService.Id, shortService.DurationInMinutes, shortService.Price);
    emp2.AssignService(tenant.Id, longService.Id, longService.DurationInMinutes, longService.Price);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Services.AddRange(shortService, longService);
    db.Employees.AddRange(emp1, emp2);
    await db.SaveChangesAsync(ct);

    var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    return new SwapEnv(db, tenant, emp1, emp2, shortService, longService, day);
  }

  public static Appointment AddAppointment(SwapEnv env, Employee employee, Service service, DateOnly date, TimeOnly start, int durationMinutes, AppointmentStatus? status = null)
  {
    var appointment = new Appointment(
      env.Tenant.Id, employee.Id, service.Id, null,
      date, start, start.AddMinutes(durationMinutes),
      status ?? AppointmentStatus.Booked,
      new Money(service.Price.Amount, "PLN"),
      string.Empty,
      null);
    env.Db.Appointments.Add(appointment);
    return appointment;
  }

  public static async Task<(Appointment First, Appointment Second)> ReloadAsync(SwapEnv env, Guid firstId, Guid secondId, CancellationToken ct)
  {
    var first = await env.Db.Appointments.AsNoTracking().FirstAsync(a => a.Id == firstId, ct);
    var second = await env.Db.Appointments.AsNoTracking().FirstAsync(a => a.Id == secondId, ct);
    return (first, second);
  }

  public static SwapAppointmentsHandler BuildSwapHandler(SwapEnv env, Guid? tenantOverride = null)
    => new(
      new AppointmentRepository(env.Db),
      new EmployeeRepository(env.Db),
      new ServiceRepository(env.Db),
      new TenantRepository(env.Db),
      env.Db,
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantOverride ?? env.Tenant.Id),
      new AppointmentService(new AppointmentRepository(env.Db)),
      new App.Application.UnitTests.Booking.CapturingPublisher(),
      NullLogger<SwapAppointmentsHandler>.Instance);

  public static PreviewSwapAppointmentsHandler BuildPreviewHandler(SwapEnv env, Guid? tenantOverride = null)
    => new(
      new AppointmentRepository(env.Db),
      new EmployeeRepository(env.Db),
      new ServiceRepository(env.Db),
      new PermissiveStaffAccessPolicy(),
      new FakeCurrentTenantService(tenantOverride ?? env.Tenant.Id),
      new AppointmentService(new AppointmentRepository(env.Db)));

  private static Employee BuildEmployee(Guid tenantId, string firstName, TimeOnly start, TimeOnly end)
  {
    var employee = new Employee(tenantId, null, firstName, "Test", $"{firstName.ToLowerInvariant()}@swap.local");
    var ranges = (IReadOnlyCollection<TimeRange>)new List<TimeRange> { new(start, end) };
    employee.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => ranges));
    return employee;
  }

  internal sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
