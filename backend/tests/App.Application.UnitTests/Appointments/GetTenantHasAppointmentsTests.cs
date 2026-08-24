using App.Application.Appointments.Queries.GetTenantHasAppointments;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Onboarding-checklist na dashboardzie — krok "Odbierz pierwszą rezerwację".
/// Lekki EXISTS zamiast pobierania listy w szerokim zakresie dat (ten miał limit 366 dni).
/// </summary>
public sealed class GetTenantHasAppointmentsTests
{
  private static ApplicationDbContext NewDb(Guid tenantId, string? dbName = null) =>
    new(
      new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
        .Options,
      new FakeCurrentTenantService(tenantId));

  private static Appointment NewAppointment(Guid tenantId, AppointmentStatus status) =>
    new(
      tenantId,
      employeeId: Guid.NewGuid(),
      serviceId: Guid.NewGuid(),
      customerId: Guid.NewGuid(),
      date: DateOnly.FromDateTime(DateTime.UtcNow),
      startTime: new TimeOnly(10, 0),
      endTime: new TimeOnly(10, 30),
      status: status,
      totalPrice: new Money(80m, "PLN"),
      appointmentNotes: string.Empty,
      lease: null);

  [Fact]
  public async Task Returns_false_when_tenant_has_no_appointments()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var db = NewDb(tenantId);

    var handler = new GetTenantHasAppointmentsHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetTenantHasAppointmentsQuery(), ct);

    Assert.False(result);
  }

  [Fact]
  public async Task Returns_true_when_a_real_appointment_exists()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var db = NewDb(tenantId);
    db.Appointments.Add(NewAppointment(tenantId, AppointmentStatus.Booked));
    await db.SaveChangesAsync(ct);

    var handler = new GetTenantHasAppointmentsHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetTenantHasAppointmentsQuery(), ct);

    Assert.True(result);
  }

  [Fact]
  public async Task Ignores_transient_AwaitingOtp_holds()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var db = NewDb(tenantId);
    db.Appointments.Add(NewAppointment(tenantId, AppointmentStatus.AwaitingOtp));
    await db.SaveChangesAsync(ct);

    var handler = new GetTenantHasAppointmentsHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetTenantHasAppointmentsQuery(), ct);

    Assert.False(result);
  }

  [Fact]
  public async Task Does_not_see_other_tenants_appointments()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    // Zapis jako obcy tenant (write-side TenantViolation pilnuje, by encja zgadzała się z kontekstem).
    var otherDb = NewDb(otherTenantId, dbName);
    otherDb.Appointments.Add(NewAppointment(otherTenantId, AppointmentStatus.Booked));
    await otherDb.SaveChangesAsync(ct);

    // Odczyt jako bieżący tenant na tej samej bazie — query filter musi odciąć cudze dane.
    var db = NewDb(tenantId, dbName);
    var handler = new GetTenantHasAppointmentsHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetTenantHasAppointmentsQuery(), ct);

    Assert.False(result);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
