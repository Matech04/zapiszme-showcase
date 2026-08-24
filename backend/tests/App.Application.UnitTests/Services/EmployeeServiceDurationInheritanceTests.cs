using App.Application.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;

namespace App.Application.UnitTests.Services;

/// <summary>
/// Regresja (prod): zmiana czasu trwania usługi (np. 20 → 15 min) w panelu nie wpływała na czas
/// nowo tworzonej wizyty. Przyczyna: przypisanie pracownika z multi-selecta w formularzu usługi
/// robiło snapshot Service.DurationInMinutes do EmployeeService.CustomDuration, który wygrywał nad
/// żywą wartością katalogu i nigdy nie był aktualizowany. Model: <c>CustomDuration == null</c> =
/// „dziedzicz z katalogu"; override ustawia się jawnie tylko w formularzu pracownika.
/// </summary>
public class EmployeeServiceDurationInheritanceTests
{
  private static Service NewService(Guid tenantId, int duration, decimal price = 80m) =>
    new(tenantId, null, Guid.NewGuid(), "Strzyżenie", new Money(price, "PLN"), duration);

  private static Employee NewEmployee(Guid tenantId) =>
    new(tenantId, Guid.NewGuid(), "Ann", "Smith", "ann@salon.pl");

  private static void EditCatalog(Service service, int newDuration, decimal newPrice) =>
    service.Update(service.CategoryId, service.VatRateId, service.Name, new Money(newPrice, "PLN"), newDuration);

  [Fact]
  public void Bulk_assigned_service_without_override_inherits_live_catalog_and_propagates_edit()
  {
    var tenantId = Guid.NewGuid();
    var employee = NewEmployee(tenantId);
    var service = NewService(tenantId, duration: 20, price: 80m);

    // Bulk-assign (formularz usługi): brak override → dziedziczy z katalogu.
    employee.AssignService(tenantId, service.Id, customDuration: null, customPrice: null);

    var before = Assert.Single(AppointmentComboBuilder.BuildLines(employee, new[] { service }));
    Assert.Equal(20, before.DurationMinutes);
    Assert.Equal(80m, before.Price.Amount);

    // Edycja usługi w panelu: 20 → 15 min, 80 → 70 zł.
    EditCatalog(service, newDuration: 15, newPrice: 70m);

    var after = Assert.Single(AppointmentComboBuilder.BuildLines(employee, new[] { service }));
    Assert.Equal(15, after.DurationMinutes); // wcześniej zostawało 20 (bug)
    Assert.Equal(70m, after.Price.Amount);
  }

  [Fact]
  public void Explicit_override_wins_over_catalog_and_survives_catalog_edit()
  {
    var tenantId = Guid.NewGuid();
    var employee = NewEmployee(tenantId);
    var service = NewService(tenantId, duration: 20, price: 80m);

    // Świadomy override per-pracownik (formularz pracownika).
    employee.AssignService(tenantId, service.Id, customDuration: 25, customPrice: new Money(99m, "PLN"));

    var before = Assert.Single(AppointmentComboBuilder.BuildLines(employee, new[] { service }));
    Assert.Equal(25, before.DurationMinutes);
    Assert.Equal(99m, before.Price.Amount);

    // Zmiana katalogu nie rusza świadomego override'u.
    EditCatalog(service, newDuration: 15, newPrice: 50m);

    var after = Assert.Single(AppointmentComboBuilder.BuildLines(employee, new[] { service }));
    Assert.Equal(25, after.DurationMinutes);
    Assert.Equal(99m, after.Price.Amount);
  }

  [Fact]
  public void Resolve_helpers_fall_back_to_default_when_override_is_null()
  {
    var tenantId = Guid.NewGuid();
    var employee = NewEmployee(tenantId);
    var serviceId = Guid.NewGuid();

    employee.AssignService(tenantId, serviceId, customDuration: null, customPrice: null);

    var link = Assert.Single(employee.Services);
    Assert.Null(link.CustomDuration);
    Assert.Null(link.CustomPrice);
    Assert.Equal(15, employee.ResolveServiceDurationMinutes(serviceId, defaultDuration: 15));
    Assert.Equal(new TimeOnly(10, 15), employee.CalculateEndTime(new TimeOnly(10, 0), serviceId, defaultDuration: 15));
  }
}
