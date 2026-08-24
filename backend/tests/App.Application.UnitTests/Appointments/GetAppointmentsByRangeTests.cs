using App.Application.Appointments.Queries.GetAppointmentsByRange;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// Kalendarz/personel — lista wizyt z zakresu dat. Wygasłe holdy OTP (AwaitingOtp z przeterminowaną
/// dzierżawą) nie są realnymi wizytami i nie powinny się pojawiać jako kafelki w kalendarzu.
/// </summary>
public sealed class GetAppointmentsByRangeTests
{
  [Fact]
  public async Task Excludes_expired_awaiting_otp_holds_but_keeps_active_holds_and_real_appointments()
  {
    var ct = TestContext.Current.CancellationToken;

    var tenant = new Tenant("Range", "range-" + Guid.NewGuid().ToString("N")[..8]);
    var tenantId = tenant.Id;
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var category = new ServiceCategory(tenant.Id, "Default", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Range", "Worker", "range@worker.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 150);

    var date = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);

    Appointment Make(int hour, AppointmentStatus status, HoldLease? lease) => new(
      tenant.Id, employee.Id, service.Id, customerId: null,
      date, new TimeOnly(hour, 0), new TimeOnly(hour, 0).AddMinutes(150),
      status, new Money(80m, "PLN"), "", lease: lease, source: AppointmentSource.Online);

    var booked = Make(9, AppointmentStatus.Booked, null);
    var activeHold = Make(12, AppointmentStatus.AwaitingOtp, new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5)));
    var expiredHold = Make(13, AppointmentStatus.AwaitingOtp, new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5)));
    var another = Make(15, AppointmentStatus.Booked, null);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.AddRange(booked, activeHold, expiredHold, another);
    await db.SaveChangesAsync(ct);

    var handler = new GetAppointmentsByRangeHandler(db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(
      new GetAppointmentsByRangeQuery(date, date, employee.Id), ct);

    var ids = result.Select(r => r.Id).ToHashSet();
    Assert.Contains(booked.Id, ids);
    Assert.Contains(another.Id, ids);
    Assert.Contains(activeHold.Id, ids);       // aktywny hold (lease ważny) wciąż widoczny
    Assert.DoesNotContain(expiredHold.Id, ids); // wygasły hold zniknął — slot faktycznie wolny
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
