using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

/// <summary>Inwarianty wizyty-combo (multi-usługa): suma czasu/ceny, primary, limity, duplikaty.</summary>
public class AppointmentComboTests
{
  private static AppointmentServiceLine Line(int duration, decimal price)
    => new(Guid.NewGuid(), duration, new Money(price, "PLN"));

  private static Appointment Create(IReadOnlyList<AppointmentServiceLine> services)
    => new(
        Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 6, 10), new TimeOnly(10, 0),
        AppointmentStatus.Booked, "", null, services);

  [Fact]
  public void Combo_sums_duration_and_price_and_sets_primary()
  {
    var s1 = Line(60, 100m);
    var s2 = Line(30, 40m);
    var s3 = Line(45, 60m);

    var appt = Create(new[] { s1, s2, s3 });

    Assert.Equal(new TimeOnly(10, 0), appt.StartTime);
    Assert.Equal(new TimeOnly(12, 15), appt.EndTime); // 135 min
    Assert.Equal(200m, appt.TotalPrice.Amount);
    Assert.Equal(s1.ServiceId, appt.ServiceId); // primary = pierwsza pozycja
    Assert.Equal(3, appt.Items.Count);
    Assert.Equal(new[] { 0, 1, 2 }, appt.Items.OrderBy(i => i.Position).Select(i => i.Position));
  }

  [Fact]
  public void Combo_requires_at_least_one_service()
  {
    var ex = Assert.Throws<AppointmentBookingRuleException>(() => Create(Array.Empty<AppointmentServiceLine>()));
    Assert.Equal(ErrorCodes.AppointmentNoServices, ex.ErrorCode);
  }

  [Fact]
  public void Combo_rejects_more_than_max_services()
  {
    var lines = Enumerable.Range(0, Appointment.MaxServices + 1).Select(_ => Line(10, 10m)).ToArray();
    var ex = Assert.Throws<AppointmentBookingRuleException>(() => Create(lines));
    Assert.Equal(ErrorCodes.AppointmentTooManyServices, ex.ErrorCode);
  }

  [Fact]
  public void Combo_rejects_duplicate_service()
  {
    var dup = Line(30, 50m);
    var ex = Assert.Throws<AppointmentBookingRuleException>(() => Create(new[] { dup, dup }));
    Assert.Equal(ErrorCodes.AppointmentDuplicateService, ex.ErrorCode);
  }

  [Fact]
  public void SingleService_constructor_builds_one_item()
  {
    var serviceId = Guid.NewGuid();
    var appt = new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), serviceId, null,
        new DateOnly(2026, 6, 10), new TimeOnly(10, 0), new TimeOnly(11, 0),
        AppointmentStatus.Booked, new Money(120m, "PLN"), "", null);

    Assert.Single(appt.Items);
    Assert.Equal(serviceId, appt.ServiceId);
    Assert.Equal(60, appt.Items.First().DurationMinutes);
    Assert.Equal(120m, appt.Items.First().PriceAmount);
  }

  [Fact]
  public void Reschedule_replaces_services_and_recomputes()
  {
    var appt = Create(new[] { Line(60, 100m) });
    var newEmp = Guid.NewGuid();
    var newServices = new[] { Line(30, 40m), Line(30, 40m) };

    appt.Reschedule(newEmp, new DateOnly(2026, 6, 11), new TimeOnly(9, 0), newServices);

    Assert.Equal(newEmp, appt.EmployeeId);
    Assert.Equal(new TimeOnly(9, 0), appt.StartTime);
    Assert.Equal(new TimeOnly(10, 0), appt.EndTime); // 60 min
    Assert.Equal(80m, appt.TotalPrice.Amount);
    Assert.Equal(2, appt.Items.Count);
  }

  // ── Niestandardowy czas trwania (override personelu) ─────────────────────────────

  [Fact]
  public void Custom_duration_in_constructor_overrides_end_time()
  {
    var appt = new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 6, 10), new TimeOnly(10, 0),
        AppointmentStatus.Booked, "", null, new[] { Line(60, 100m) },
        customDurationMinutes: 40);

    Assert.Equal(new TimeOnly(10, 40), appt.EndTime); // 40 zamiast 60
    Assert.Equal(40, appt.CustomDurationMinutes);
    Assert.Equal(60, appt.Items.Single().DurationMinutes); // pozycja trzyma standardowy czas usługi
  }

  [Fact]
  public void Custom_duration_equal_to_standard_is_normalized_to_null()
  {
    var appt = new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 6, 10), new TimeOnly(10, 0),
        AppointmentStatus.Booked, "", null, new[] { Line(60, 100m) },
        customDurationMinutes: 60);

    Assert.Null(appt.CustomDurationMinutes); // == standard → czas standardowy
    Assert.Equal(new TimeOnly(11, 0), appt.EndTime);
  }

  [Fact]
  public void SetCustomDuration_shortens_and_can_reset_to_standard()
  {
    var appt = Create(new[] { Line(60, 100m) });

    appt.SetCustomDuration(40);
    Assert.Equal(40, appt.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(10, 40), appt.EndTime);

    appt.SetCustomDuration(null); // powrót do standardu
    Assert.Null(appt.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(11, 0), appt.EndTime);
  }

  [Fact]
  public void SetCustomDuration_rejects_non_positive()
  {
    var appt = Create(new[] { Line(60, 100m) });
    var ex = Assert.Throws<AppointmentBookingRuleException>(() => appt.SetCustomDuration(0));
    Assert.Equal(ErrorCodes.AppointmentInvalidDuration, ex.ErrorCode);
  }

  [Fact]
  public void SetCustomDuration_rejects_wrap_past_midnight()
  {
    var appt = new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 6, 10), new TimeOnly(23, 0),
        AppointmentStatus.Booked, "", null, new[] { Line(30, 50m) });

    // 23:00 + 180 min zawija za północ → InvalidTimeRangeException (start >= end).
    Assert.ThrowsAny<Exception>(() => appt.SetCustomDuration(180));
  }

  [Fact]
  public void Custom_duration_is_preserved_across_reschedule_when_not_specified()
  {
    var appt = Create(new[] { Line(60, 100m) });
    appt.SetCustomDuration(40);

    // Zwykłe przesunięcie terminu (bez podania czasu) — override zostaje.
    appt.Reschedule(appt.EmployeeId, new DateOnly(2026, 6, 11), new TimeOnly(9, 0), new[] { Line(60, 100m) });

    Assert.Equal(40, appt.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(9, 40), appt.EndTime);
  }

  [Fact]
  public void Reschedule_with_explicit_duration_overrides()
  {
    var appt = Create(new[] { Line(60, 100m) });

    appt.Reschedule(appt.EmployeeId, new DateOnly(2026, 6, 11), new TimeOnly(9, 0), new[] { Line(60, 100m) }, customDurationMinutes: 75);

    Assert.Equal(75, appt.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(10, 15), appt.EndTime);
  }
}
