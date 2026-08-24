using App.Application.Booking.BookingServices.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// GetBookingServicesQuery — wariant per-pracownik: zwraca WYŁĄCZNIE usługi tego pracownika, z ceną/
/// czasem zresolvowanym (override CustomPrice/CustomDuration, fallback do katalogu). To sedno poprawki:
/// klient widzi właściwą cenę pracownika (np. 220 zł), a nie domyślną katalogową (180 zł).
/// </summary>
public sealed class GetBookingServicesQueryTests
{
  [Fact]
  public async Task Employee_variant_resolves_custom_price_and_duration()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, catalogSvc, empId) = SetupWithOverride(
      catalogPrice: 180m, catalogDuration: 60,
      customPrice: 220m, customDuration: 75);
    var handler = new GetBookingServicesQueryHandler(db, new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingServicesQuery(null, empId), ct);

    var dto = Assert.Single(result);
    Assert.Equal(catalogSvc, dto.Id);
    Assert.Equal(220m, dto.Price.Amount); // override, nie katalogowe 180
    Assert.Equal(75, dto.DurationInMinutes);
  }

  [Fact]
  public async Task Employee_variant_falls_back_to_catalog_when_no_override()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, catalogSvc, empId) = SetupWithOverride(
      catalogPrice: 180m, catalogDuration: 60,
      customPrice: null, customDuration: null);
    var handler = new GetBookingServicesQueryHandler(db, new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingServicesQuery(null, empId), ct);

    var dto = Assert.Single(result);
    Assert.Equal(180m, dto.Price.Amount);
    Assert.Equal(60, dto.DurationInMinutes);
  }

  [Fact]
  public async Task Employee_variant_returns_only_services_that_employee_offers()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantId = System.Guid.NewGuid();
    var db = NewDb(tenantId);
    var vat = new VatRate(tenantId, "VAT", 0.23m);
    var offered = new Service(tenantId, null, vat.Id, "Manicure", new Money(180m, "PLN"), 60);
    var notOffered = new Service(tenantId, null, vat.Id, "Pedicure", new Money(150m, "PLN"), 90);
    var magda = new Employee(tenantId, null, "Magda", "B", "magda@salon.local");
    magda.AssignService(tenantId, offered.Id, null, new Money(220m, "PLN"));
    db.VatRates.Add(vat);
    db.Services.AddRange(offered, notOffered);
    db.Employees.Add(magda);
    db.SaveChanges();
    var handler = new GetBookingServicesQueryHandler(db, new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingServicesQuery(null, magda.Id), ct);

    var dto = Assert.Single(result);
    Assert.Equal(offered.Id, dto.Id);
  }

  [Fact]
  public async Task Catalog_variant_ignores_employee_overrides()
  {
    // Bez EmployeeId → cena katalogowa (zachowanie jak dotąd).
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupWithOverride(
      catalogPrice: 180m, catalogDuration: 60,
      customPrice: 220m, customDuration: 75);
    var handler = new GetBookingServicesQueryHandler(db, new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingServicesQuery(null), ct);

    var dto = Assert.Single(result);
    Assert.Equal(180m, dto.Price.Amount);
    Assert.Equal(60, dto.DurationInMinutes);
  }

  [Fact]
  public async Task Employee_variant_returns_empty_for_unknown_employee()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _) = SetupWithOverride(180m, 60, null, null);
    var handler = new GetBookingServicesQueryHandler(db, new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingServicesQuery(null, System.Guid.NewGuid()), ct);

    Assert.Empty(result);
  }

  private static (ApplicationDbContext db, System.Guid tenantId, System.Guid serviceId, System.Guid employeeId) SetupWithOverride(
    decimal catalogPrice, int catalogDuration, decimal? customPrice, int? customDuration)
  {
    var tenantId = System.Guid.NewGuid();
    var db = NewDb(tenantId);
    var vat = new VatRate(tenantId, "VAT", 0.23m);
    var svc = new Service(tenantId, null, vat.Id, "Manicure hybrydowy", new Money(catalogPrice, "PLN"), catalogDuration);
    var magda = new Employee(tenantId, null, "Magda", "Nowak", "magda@salon.local");
    magda.AssignService(tenantId, svc.Id, customDuration, customPrice is { } p ? new Money(p, "PLN") : null);
    db.VatRates.Add(vat);
    db.Services.Add(svc);
    db.Employees.Add(magda);
    db.SaveChanges();
    return (db, tenantId, svc.Id, magda.Id);
  }

  private static ApplicationDbContext NewDb(System.Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public System.Guid? TenantId { get; }
    public FakeCurrentTenantService(System.Guid tenantId) => TenantId = tenantId;
  }
}
