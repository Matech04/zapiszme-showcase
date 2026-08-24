using App.Application.Appointments;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Application.UnitTests.Appointments;

public class GapFillingSlotFilterTests
{
  private static TimeOnly T(int h, int m) => new(h, m);
  private static TimeRange R(int h1, int m1, int h2, int m2) => new(T(h1, m1), T(h2, m2));

  private static GapFillingSettings Prefer(int buffer = 0, int lookahead = 1)
    => new(GapFillingMode.PreferAdjacent, buffer, lookahead);

  private static GapFillingSettings Adjacent(int buffer = 0, int lookahead = 1)
    => new(GapFillingMode.AdjacentOnly, buffer, lookahead);

  [Fact]
  public void Disabled_returns_all_slots_with_isPreferred_false()
  {
    var slots = new[] { T(10, 0), T(10, 30), T(11, 0) };
    var appts = new[] { R(11, 30, 12, 30) };
    var settings = new GapFillingSettings(GapFillingMode.Disabled, 0, 1);

    var result = GapFillingSlotFilter.Apply(slots, appts, 30, 30, settings);

    Assert.Equal(3, result.Count);
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public void Null_settings_returns_all_slots_with_isPreferred_false()
  {
    var slots = new[] { T(10, 0), T(11, 0) };
    var result = GapFillingSlotFilter.Apply(slots, Array.Empty<TimeRange>(), 60, 60, null);

    Assert.Equal(2, result.Count);
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public void PreferAdjacent_marks_slot_directly_before_appointment_as_preferred()
  {
    // Appointment at 11:00–12:00, service duration 60 min, slot step 60 min.
    // Slot starting at 10:00 ends at 11:00 = appt.Start → preferred.
    var slots = new[] { T(9, 0), T(10, 0), T(13, 0) };
    var appts = new[] { R(11, 0, 12, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer());

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["09:00"]);
    Assert.True(map["10:00"]);
    Assert.False(map["13:00"]);
  }

  [Fact]
  public void PreferAdjacent_marks_slot_directly_after_appointment_as_preferred()
  {
    // Appointment at 09:00–10:00, service duration 60 min, slot step 60 min.
    // Slot starting at 10:00 = appt.End → preferred.
    var slots = new[] { T(7, 0), T(10, 0), T(11, 0) };
    var appts = new[] { R(9, 0, 10, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer());

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["07:00"]);
    Assert.True(map["10:00"]);
    Assert.False(map["11:00"]);
  }

  [Fact]
  public void PreferAdjacent_with_buffer_shifts_adjacency()
  {
    // Wizyta 11:00–12:00, buffer=10, service=60, step=60.
    // After: slot.Start ∈ [12:10, 13:10). 12:00 < 12:10 → poza oknem → niepreferowane.
    // Before: slot.End ∈ (09:50, 10:50] ⇔ slot.Start ∈ (08:50, 09:50] (dur=60).
    //   slots: 09:00 ∈ (08:50, 09:50] → preferowane; 10:00 → end=11:00 > 10:50 → nie.
    // Buffer respektowany: slot graniczący z wizytą musi kończyć się przed (apptStart - buffer).
    var slots = new[] { T(9, 0), T(10, 0), T(12, 0) };
    var appts = new[] { R(11, 0, 12, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer(buffer: 10));

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.True(map["09:00"]);
    Assert.False(map["10:00"]);
    Assert.False(map["12:00"]);
  }

  [Fact]
  public void PreferAdjacent_marks_adjacent_slots_when_service_duration_off_grid()
  {
    // Regresja: usługa 40 min na slotStep=15 → granice wizyt nie padają na grid.
    // Sloty z całego dnia, wizyty 10:00–10:40, 10:45–11:25, 11:30–12:10.
    // Sąsiedzi: 09:15 (przed pierwszą — slot 09:15 + 40 = 09:55 ∈ (08:45, 10:00])
    //          oraz 12:15 (po ostatniej — slot 12:15 ∈ [12:10, 12:25)).
    var slots = new[]
    {
      T(9, 0), T(9, 15),
      T(12, 15), T(12, 30), T(12, 45), T(13, 0)
    };
    var appts = new[]
    {
      R(10, 0, 10, 40),
      R(10, 45, 11, 25),
      R(11, 30, 12, 10),
    };

    var result = GapFillingSlotFilter.Apply(slots, appts, 40, 15, Prefer());

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["09:00"]);
    Assert.True(map["09:15"]);
    Assert.True(map["12:15"]);
    Assert.False(map["12:30"]);
    Assert.False(map["12:45"]);
    Assert.False(map["13:00"]);
  }

  [Fact]
  public void PreferAdjacent_with_buffer_marks_correct_after_slot()
  {
    // Appointment at 09:00–10:00, buffer=15, service=45, step=15.
    // After: slot.Start = 10:00 + 15 = 10:15 → preferred if in raw slots.
    var slots = new[] { T(10, 0), T(10, 15), T(10, 30) };
    var appts = new[] { R(9, 0, 10, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 45, 15, Prefer(buffer: 15));

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["10:00"]);
    Assert.True(map["10:15"]);
    Assert.False(map["10:30"]);
  }

  [Fact]
  public void PreferAdjacent_lookahead_2_marks_two_slots_before_appointment()
  {
    // Appointment at 12:00–13:00, service=60, step=60, lookahead=2.
    // Before[0]: slot.Start = 12:00 - 60 = 11:00 → preferred.
    // Before[1]: slot.Start = 12:00 - 60 - 60 = 10:00 → preferred.
    var slots = new[] { T(9, 0), T(10, 0), T(11, 0), T(15, 0) };
    var appts = new[] { R(12, 0, 13, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer(lookahead: 2));

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["09:00"]);
    Assert.True(map["10:00"]);
    Assert.True(map["11:00"]);
    Assert.False(map["15:00"]);
  }

  [Fact]
  public void PreferAdjacent_lookahead_2_marks_two_slots_after_appointment()
  {
    // Appointment at 09:00–10:00, service=60, step=60, lookahead=2.
    // After[0]: slot.Start = 10:00 → preferred.
    // After[1]: slot.Start = 10:00 + 60 = 11:00 → preferred.
    var slots = new[] { T(6, 0), T(10, 0), T(11, 0), T(12, 0) };
    var appts = new[] { R(9, 0, 10, 0) };

    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer(lookahead: 2));

    var map = result.ToDictionary(r => r.slot, r => r.isPreferred);
    Assert.False(map["06:00"]);
    Assert.True(map["10:00"]);
    Assert.True(map["11:00"]);
    Assert.False(map["12:00"]);
  }

  [Fact]
  public void AdjacentOnly_returns_only_preferred_slots()
  {
    var slots = new[] { T(9, 0), T(10, 0), T(11, 0), T(13, 0) };
    var appts = new[] { R(11, 0, 12, 0) };

    // Slot starting at 10:00 ends at 11:00 = appt.Start → preferred.
    // Slot starting at 12:00 would be after, but 12:00 not in slots.
    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Adjacent());

    Assert.Single(result);
    Assert.Equal("10:00", result[0].slot);
    Assert.True(result[0].isPreferred);
  }

  [Fact]
  public void AdjacentOnly_with_no_appointments_fallback_returns_all_with_isPreferred_false()
  {
    var slots = new[] { T(10, 0), T(11, 0), T(12, 0) };

    var result = GapFillingSlotFilter.Apply(slots, Array.Empty<TimeRange>(), 60, 60, Adjacent());

    Assert.Equal(3, result.Count);
    Assert.All(result, r => Assert.False(r.isPreferred));
  }

  [Fact]
  public void PreferAdjacent_returns_all_slots_even_when_none_preferred()
  {
    var slots = new[] { T(10, 0), T(11, 0) };
    var appts = new[] { R(14, 0, 15, 0) };

    // No slots adjacent to 14:00 appointment within these raw slots.
    var result = GapFillingSlotFilter.Apply(slots, appts, 60, 60, Prefer());

    Assert.Equal(2, result.Count);
    Assert.All(result, r => Assert.False(r.isPreferred));
  }
}
