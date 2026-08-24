using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

/// <summary>Usługi dodatkowe (add-ony): czas 0, powiązania main→addon, blokada wizyty 0-min.</summary>
public class ServiceAddonTests
{
  private static Service Svc(int duration = 60, bool isAddon = false)
    => new(Guid.NewGuid(), null, Guid.NewGuid(), "Usługa", new Money(100m, "PLN"), duration, isAddon: isAddon);

  [Fact]
  public void Service_CanHaveZeroDuration()
  {
    var addon = Svc(duration: 0, isAddon: true);

    Assert.Equal(0, addon.DurationInMinutes);
    Assert.True(addon.IsAddon);
  }

  [Fact]
  public void ServiceUpdate_CanSetZeroDuration()
  {
    var service = Svc();

    service.Update(null, Guid.NewGuid(), "Usługa", new Money(100m, "PLN"), 0, isAddon: true);

    Assert.Equal(0, service.DurationInMinutes);
    Assert.True(service.IsAddon);
  }

  [Fact]
  public void SetAddons_ReconcilesList_AddsRemovesAndDedupes()
  {
    var main = Svc();
    var a1 = Guid.NewGuid();
    var a2 = Guid.NewGuid();

    main.SetAddons(new[] { a1, a2, a1 }); // duplikat ignorowany
    Assert.Equal(2, main.Addons.Count);

    main.SetAddons(new[] { a2 }); // a1 usunięty
    Assert.Single(main.Addons);
    Assert.Equal(a2, main.Addons.Single().AddonServiceId);
  }

  [Fact]
  public void SetAddons_SkipsSelfReference()
  {
    var main = Svc();

    main.SetAddons(new[] { main.Id, Guid.NewGuid() });

    Assert.Single(main.Addons);
    Assert.DoesNotContain(main.Addons, a => a.AddonServiceId == main.Id);
  }

  [Fact]
  public void SetAddons_OnAddonService_StaysEmpty()
  {
    var addon = Svc(duration: 0, isAddon: true);

    addon.SetAddons(new[] { Guid.NewGuid() });

    Assert.Empty(addon.Addons);
  }

  [Fact]
  public void Update_TurningServiceIntoAddon_ClearsAddons()
  {
    var main = Svc();
    main.SetAddons(new[] { Guid.NewGuid() });
    Assert.NotEmpty(main.Addons);

    main.Update(null, Guid.NewGuid(), "Usługa", new Money(100m, "PLN"), 0, isAddon: true);

    Assert.Empty(main.Addons);
  }

  [Fact]
  public void SetServices_WithZeroTotalDuration_Throws()
  {
    var line = new AppointmentServiceLine(Guid.NewGuid(), 0, new Money(50m, "PLN"));

    var ex = Assert.Throws<AppointmentBookingRuleException>(() => new Appointment(
      Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 6, 5), new TimeOnly(10, 0),
      AppointmentStatus.Booked, string.Empty, null, new[] { line }));

    Assert.Equal(ErrorCodes.AppointmentZeroDuration, ex.ErrorCode);
  }

  [Fact]
  public void SetServices_WithTimedMainPlusZeroAddon_Succeeds()
  {
    var main = new AppointmentServiceLine(Guid.NewGuid(), 60, new Money(100m, "PLN"));
    var addon = new AppointmentServiceLine(Guid.NewGuid(), 0, new Money(20m, "PLN"));

    var appt = new Appointment(
      Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 6, 5), new TimeOnly(10, 0),
      AppointmentStatus.Booked, string.Empty, null, new[] { main, addon });

    Assert.Equal(new TimeOnly(11, 0), appt.EndTime); // 60 + 0 minut
    Assert.Equal(2, appt.Items.Count);
  }
}
