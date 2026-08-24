using App.Application.Common.Interfaces;
using App.Application.Notifications.Queries.GetCustomerChangedAppointments;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-CHANGE-* — powiadomienia o zmianach zrobionych przez klienta (anulowanie/przełożenie).
/// Dzwonek jest osobisty: pracownik widzi tylko swoje wizyty, konto „Recepcja" (Kiosk) całe salon.
/// DTO niesie <c>EmployeeId</c>, bo dashboard linkuje powiadomienie prosto na
/// /admin/schedule/:employeeId — bez tego kalendarz przechodził przez redirect z gołej trasy
/// (przeładowanie komponentu i widoczny przeskok przez „dziś").
/// </summary>
public sealed class GetCustomerChangedAppointmentsQueryTests
{
  [Fact]
  public async Task ReturnsEmployeeId_ForDeepLinkWithoutRedirect()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      var appt = NewAppointment(tenantId, employeeId);
      appt.RecordSelfServiceChange(1); // 1 = anulowana przez klienta
      seed.Appointments.Add(appt);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new GetCustomerChangedAppointmentsQueryHandler(
      db, new FakeCurrentTenantService(tenantId), Desk());

    var result = await handler.Handle(new GetCustomerChangedAppointmentsQuery(), ct);

    var item = Assert.Single(result);
    Assert.Equal(employeeId, item.EmployeeId);
    Assert.Equal(1, item.ChangeKind);
  }

  [Fact]
  public async Task Employee_SeesOnlyOwnAppointments()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var me = Guid.NewGuid();
    var colleague = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      var mine = NewAppointment(tenantId, me);
      mine.RecordSelfServiceChange(2); // 2 = przełożona przez klienta
      var hers = NewAppointment(tenantId, colleague);
      hers.RecordSelfServiceChange(1);
      seed.Appointments.AddRange(mine, hers);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new GetCustomerChangedAppointmentsQueryHandler(
      db, new FakeCurrentTenantService(tenantId), Staff(me));

    var result = await handler.Handle(new GetCustomerChangedAppointmentsQuery(), ct);

    var item = Assert.Single(result);
    Assert.Equal(me, item.EmployeeId);
  }

  [Fact]
  public async Task OtherTenantChanges_AreNotVisible()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var dbName = Guid.NewGuid().ToString();

    using (var seed = NewDb(dbName, null))
    {
      var foreign = NewAppointment(otherTenantId, Guid.NewGuid());
      foreign.RecordSelfServiceChange(1);
      seed.Appointments.Add(foreign);
      await seed.SaveChangesAsync(ct);
    }

    using var db = NewDb(dbName, tenantId);
    var handler = new GetCustomerChangedAppointmentsQueryHandler(
      db, new FakeCurrentTenantService(tenantId), Desk());

    Assert.Empty(await handler.Handle(new GetCustomerChangedAppointmentsQuery(), ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static Appointment NewAppointment(Guid tenantId, Guid employeeId) => new(
    tenantId,
    employeeId,
    serviceId: Guid.NewGuid(),
    customerId: Guid.NewGuid(),
    date: DateOnly.FromDateTime(DateTime.UtcNow.Date),
    startTime: new TimeOnly(10, 0),
    endTime: new TimeOnly(10, 45),
    status: AppointmentStatus.Booked,
    totalPrice: new Money(100, "PLN"),
    appointmentNotes: string.Empty,
    lease: null);

  private static FakeCurrentUser Staff(Guid employeeId) => new(employeeId, isDesk: false);
  private static FakeCurrentUser Desk() => new(null, isDesk: true);

  private static ApplicationDbContext NewDb(string dbName, Guid? currentTenant)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(dbName)
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(currentTenant));
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid? tenantId) => TenantId = tenantId;
  }

  private sealed class FakeCurrentUser : ICurrentUserAccessor
  {
    public FakeCurrentUser(Guid? callerEmployeeId, bool isDesk)
    {
      CallerEmployeeId = callerEmployeeId;
      IsDeskAccount = isDesk;
    }

    public Guid? UserId => Guid.NewGuid();
    public Guid? CallerEmployeeId { get; }
    public bool CanManageOtherEmployees => false;
    public bool IsSystemAdmin => false;
    public bool IsDeskAccount { get; }
  }
}
