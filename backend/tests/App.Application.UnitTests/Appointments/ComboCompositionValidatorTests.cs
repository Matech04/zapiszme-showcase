using App.Application.Common;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Application.UnitTests.Appointments;

/// <summary>Reguła „ten sam typ": max 1 usługa z danej (niepustej) grupy wariantów w combo.</summary>
public class ComboCompositionValidatorTests
{
  private static Service Svc(string name, string? comboGroup)
    => new(Guid.NewGuid(), null, Guid.NewGuid(), name, new Money(100m, "PLN"), 60, comboGroup: comboGroup);

  private static Service Main(string name = "Manicure")
    => new(Guid.NewGuid(), null, Guid.NewGuid(), name, new Money(100m, "PLN"), 60);

  private static Service Addon(string name = "French")
    => new(Guid.NewGuid(), null, Guid.NewGuid(), name, new Money(20m, "PLN"), 0, isAddon: true);

  [Fact]
  public void Allows_different_groups_and_groupless()
  {
    // Przedłużanie (grupa) + Cyrkonie (bez grupy) + Pedicure (inna grupa) — DOZWOLONE.
    var services = new[]
    {
      Svc("Przedłużanie S", "przedluzanie"),
      Svc("Cyrkonie", null),
      Svc("Pedicure", "pedicure"),
    };

    var ex = Record.Exception(() => ComboCompositionValidator.EnsureValidComposition(services));
    Assert.Null(ex);
  }

  [Fact]
  public void Forbids_two_services_from_same_group()
  {
    // Przedłużanie S + Przedłużanie XL (ta sama grupa) — ZAKAZANE.
    var services = new[]
    {
      Svc("Przedłużanie S", "Przedłużanie"),
      Svc("Przedłużanie XL", "przedłużanie "), // inny case/spacja → ta sama grupa
    };

    var ex = Assert.Throws<AppointmentBookingRuleException>(
      () => ComboCompositionValidator.EnsureValidComposition(services));
    Assert.Equal(ErrorCodes.AppointmentComboGroupConflict, ex.ErrorCode);
  }

  [Fact]
  public void Allows_multiple_groupless_services()
  {
    var services = new[] { Svc("A", null), Svc("B", null), Svc("C", "") };
    var ex = Record.Exception(() => ComboCompositionValidator.EnsureValidComposition(services));
    Assert.Null(ex);
  }

  [Fact]
  public void Rejects_empty_composition()
  {
    var ex = Assert.Throws<AppointmentBookingRuleException>(
      () => ComboCompositionValidator.EnsureValidComposition(Array.Empty<Service>()));
    Assert.Equal(ErrorCodes.AppointmentNoServices, ex.ErrorCode);
  }

  // --- Dodatki ---

  [Fact]
  public void Forbids_addon_without_main_service()
  {
    var addon = Addon();

    var ex = Assert.Throws<AppointmentBookingRuleException>(
      () => ComboCompositionValidator.EnsureValidComposition(new[] { addon }));
    Assert.Equal(ErrorCodes.AppointmentAddonRequiresMain, ex.ErrorCode);
  }

  [Fact]
  public void Forbids_addon_not_allowed_by_any_main()
  {
    var main = Main();           // bez powiązań
    var addon = Addon();

    var ex = Assert.Throws<AppointmentBookingRuleException>(
      () => ComboCompositionValidator.EnsureValidComposition(new[] { main, addon }));
    Assert.Equal(ErrorCodes.AppointmentAddonNotAllowed, ex.ErrorCode);
  }

  [Fact]
  public void Allows_addon_permitted_by_main()
  {
    var main = Main();
    var addon = Addon();
    main.SetAddons(new[] { addon.Id }); // main pozwala na ten dodatek

    var ex = Record.Exception(() => ComboCompositionValidator.EnsureValidComposition(new[] { main, addon }));
    Assert.Null(ex);
  }
}
