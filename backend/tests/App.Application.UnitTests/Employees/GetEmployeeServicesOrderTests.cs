using App.Application.Common.Interfaces;
using App.Application.Employees.Queries.GetEmployeeServices;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Employees;

/// <summary>
/// APP-EMP: kolejność wyniku <see cref="GetEmployeeServicesQuery"/>.
///
/// Handler długo nie miał żadnego sortowania i zwracał przypisania w kolejności, jaką odda
/// baza — dowolnej i zmiennej po edycji przypisań. Konsumenci (panel wizyt, formularz
/// pracownika) pokazują tę listę wprost, więc kolejność musi iść za katalogiem.
/// </summary>
public sealed class GetEmployeeServicesOrderTests
{
  [Fact]
  public async Task Returns_services_in_catalog_order_regardless_of_assignment_order()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = Setup();

    // Katalog: kolejność wyznacza OrderIndex, nie moment dodania.
    var third = AddService(db, tenantId, "Trzecia", orderIndex: 30);
    var first = AddService(db, tenantId, "Pierwsza", orderIndex: 10);
    var second = AddService(db, tenantId, "Druga", orderIndex: 20);
    await db.SaveChangesAsync(ct);

    // Przypisania celowo w odwrotnej kolejności.
    employee.AssignService(tenantId, third.Id, null, null);
    employee.AssignService(tenantId, second.Id, null, null);
    employee.AssignService(tenantId, first.Id, null, null);
    await db.SaveChangesAsync(ct);

    var result = await Handle(db, tenantId, employee.Id, ct);

    Assert.Equal(new[] { first.Id, second.Id, third.Id }, result.Select(r => r.ServiceId));
  }

  [Fact]
  public async Task Breaks_ties_on_name_like_the_catalog_does()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = Setup();

    var zebra = AddService(db, tenantId, "Zabiegi", orderIndex: 0);
    var apple = AddService(db, tenantId, "Ampułka", orderIndex: 0);
    await db.SaveChangesAsync(ct);

    employee.AssignService(tenantId, zebra.Id, null, null);
    employee.AssignService(tenantId, apple.Id, null, null);
    await db.SaveChangesAsync(ct);

    var result = await Handle(db, tenantId, employee.Id, ct);

    Assert.Equal(new[] { apple.Id, zebra.Id }, result.Select(r => r.ServiceId));
  }

  /// <summary>
  /// Sortowanie nie może gubić przypisań — usługa spoza katalogu (odcięta filtrem
  /// globalnym jako nieaktywna) ma zostać na liście, tylko na końcu.
  /// </summary>
  [Fact]
  public async Task Keeps_assignments_whose_service_is_not_in_the_catalog()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = Setup();

    var active = AddService(db, tenantId, "Aktywna", orderIndex: 50);
    var inactive = AddService(db, tenantId, "Nieaktywna", orderIndex: 1);
    inactive.Deactivate();
    await db.SaveChangesAsync(ct);

    employee.AssignService(tenantId, inactive.Id, null, null);
    employee.AssignService(tenantId, active.Id, null, null);
    await db.SaveChangesAsync(ct);

    var result = await Handle(db, tenantId, employee.Id, ct);

    Assert.Equal(new[] { active.Id, inactive.Id }, result.Select(r => r.ServiceId));
  }

  [Fact]
  public async Task Preserves_per_employee_overrides()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, employee) = Setup();

    var service = AddService(db, tenantId, "Manicure", orderIndex: 0);
    await db.SaveChangesAsync(ct);

    employee.AssignService(tenantId, service.Id, 45, new Money(120m, "PLN"));
    await db.SaveChangesAsync(ct);

    var row = Assert.Single(await Handle(db, tenantId, employee.Id, ct));
    Assert.Equal(45, row.CustomDuration);
    Assert.Equal(120m, row.CustomPrice?.Amount);
  }

  private static async Task<List<App.Application.Employees.Dtos.EmployeeServiceDto>> Handle(
    ApplicationDbContext db, Guid tenantId, Guid employeeId, CancellationToken ct)
  {
    var handler = new GetEmployeeServicesQueryHandler(db, new PermissiveStaffAccessPolicy(), new FakeTenant(tenantId));
    return await handler.Handle(new GetEmployeeServicesQuery(employeeId), ct);
  }

  private static Service AddService(ApplicationDbContext db, Guid tenantId, string name, int orderIndex)
  {
    var vat = new VatRate(tenantId, "VAT", 0.23m);
    db.Set<VatRate>().Add(vat);
    var service = new Service(tenantId, null, vat.Id, name, new Money(100m, "PLN"), 30);
    service.SetOrder(orderIndex);
    db.Services.Add(service);
    return service;
  }

  private static (ApplicationDbContext db, Guid tenantId, Employee employee) Setup()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeTenant(tenantId));
    var employee = new Employee(tenantId, null, "Test", "Worker", "test@e.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return (db, tenantId, employee);
  }

  private sealed class FakeTenant : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeTenant(Guid tenantId) => TenantId = tenantId;
  }
}
