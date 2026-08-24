using App.Application.Appointments.Commands.SetFinalPrice;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// SetFinalPriceHandler — happy-path (Completed), NotFound, oraz blokada dla wizyty anulowanej.
/// </summary>
public sealed class SetFinalPriceHandlerTests
{
  [Fact]
  public async Task SetFinalPrice_persists_final_price_on_completed_appointment()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, appointment) = SetupDb(AppointmentStatus.Completed);
    var handler = new SetFinalPriceHandler(new AppointmentRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var resultId = await handler.Handle(new SetFinalPriceCommand(appointment.Id, 180m, "PLN"), ct);

    Assert.Equal(appointment.Id, resultId);
    var reloaded = await db.Appointments.FindAsync(new object[] { appointment.Id }, ct);
    Assert.NotNull(reloaded!.FinalPrice);
    Assert.Equal(180m, reloaded.FinalPrice!.Amount);
    Assert.Equal("PLN", reloaded.FinalPrice.Currency);
  }

  [Fact]
  public async Task SetFinalPrice_throws_NotFound_for_unknown_appointment()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _) = SetupDb(AppointmentStatus.Completed);
    var handler = new SetFinalPriceHandler(new AppointmentRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetFinalPriceCommand(Guid.NewGuid(), 50m, "PLN"), ct));
  }

  [Fact]
  public async Task SetFinalPrice_for_appointment_from_other_tenant_throws_TenantViolation()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _, appointment) = SetupDb(AppointmentStatus.Completed);
    // Handler z innym tenantem niż wizyta — strażnik TenantId w handlerze odrzuca dostęp.
    var handler = new SetFinalPriceHandler(new AppointmentRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(Guid.NewGuid()));

    await Assert.ThrowsAsync<TenantViolation>(() =>
      handler.Handle(new SetFinalPriceCommand(appointment.Id, 50m, "PLN"), ct));
  }

  [Fact]
  public async Task SetFinalPrice_on_canceled_appointment_throws_rule_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, appointment) = SetupDb(AppointmentStatus.Canceled);
    var handler = new SetFinalPriceHandler(new AppointmentRepository(db), db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new SetFinalPriceCommand(appointment.Id, 50m, "PLN"), ct));
    Assert.Equal(ErrorCodes.AppointmentFinalPriceInvalidStatus, ex.ErrorCode);
  }

  private static (ApplicationDbContext db, Guid tenantId, Appointment appointment) SetupDb(AppointmentStatus status)
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var appointment = new Appointment(
      tenantId, Guid.NewGuid(), Guid.NewGuid(), null,
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), new TimeOnly(10, 0), new TimeOnly(11, 0),
      status, new Money(100m, "PLN"), string.Empty, null);

    db.Appointments.Add(appointment);
    db.SaveChanges();

    return (db, tenantId, appointment);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
