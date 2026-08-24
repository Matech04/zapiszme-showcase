using App.Application.Appointments.Queries.GetCustomerAppointments;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
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
/// APP-APPT-HISTORY — historia wizyt klienta (GetCustomerAppointments).
/// Regresja: historia MUSI być zachowana także po dezaktywacji (soft-delete) pracownika lub usługi —
/// joiny korzystają z <c>IgnoreQueryFilters()</c>, inaczej globalny filtr `IsActive` cicho wycinałby
/// takie wizyty z historii i z liczników KPI w profilu klienta.
/// </summary>
public sealed class GetCustomerAppointmentsHandlerTests
{
  [Fact]
  public async Task History_includes_appointments_with_deactivated_employee_and_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, customer, employee, service) = await SetupAsync(ct);

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      pastDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Completed, new Money(80m, "PLN"), string.Empty, null));
    await db.SaveChangesAsync(ct);

    // Pracownik odchodzi z salonu, usługa wycofana z oferty — oba soft-delete.
    employee.Deactivate();
    service.Deactivate();
    await db.SaveChangesAsync(ct);

    var handler = new GetCustomerAppointmentsHandler(db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenant.Id));
    var result = await handler.Handle(new GetCustomerAppointmentsQuery(customer.Id), ct);

    Assert.Single(result);
    Assert.Equal("Ann", result[0].EmployeeFirstName);
    Assert.Equal("Cut", result[0].ServiceName);
    Assert.Equal(AppointmentStatus.Completed, result[0].Status);
  }

  [Fact]
  public async Task History_returns_only_target_customer_sorted_newest_first()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, customer, employee, service) = await SetupAsync(ct);

    var other = new Customer(tenant.Id, "Ewa", "Inna", "other@e.co", new PhoneNumber("+48555000111"), "");
    db.Customers.Add(other);

    var older = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    var newer = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      older, new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Completed, new Money(80m, "PLN"), string.Empty, null));
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      newer, new TimeOnly(12, 0), new TimeOnly(12, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    // Wizyta innego klienta — NIE może trafić do historii naszego.
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, service.Id, other.Id,
      newer, new TimeOnly(15, 0), new TimeOnly(15, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    await db.SaveChangesAsync(ct);

    var handler = new GetCustomerAppointmentsHandler(db, new PermissiveStaffAccessPolicy(), new FakeCurrentTenantService(tenant.Id));
    var result = await handler.Handle(new GetCustomerAppointmentsQuery(customer.Id), ct);

    Assert.Equal(2, result.Count);
    Assert.Equal(newer, result[0].Date); // najnowsza pierwsza
    Assert.Equal(older, result[1].Date);
  }

  private static async Task<(ApplicationDbContext db, Tenant tenant, Customer customer, Employee employee, Service service)> SetupAsync(
    CancellationToken ct)
  {
    var tenant = new Tenant("History Salon", "history-" + Guid.NewGuid().ToString("N")[..8]);
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenant.Id));

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Ann", "Smith", "ann@salon.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Cut", new Money(80m, "PLN"), 30);
    var customer = new Customer(tenant.Id, "Jan", "Kowalski", "customer@e.co", new PhoneNumber("+48501234567"), "");

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);

    return (db, tenant, customer, employee, service);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
