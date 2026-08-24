using App.Application.Appointments.Dtos;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Application.Appointments;

/// <summary>
/// Applies gap-filling logic to a raw list of available slots.
/// When GapFillingMode is Disabled (or settings is null), all slots are returned
/// with IsPreferred=false. Otherwise, slots adjacent to existing appointments
/// are identified as preferred.
/// </summary>
public static class GapFillingSlotFilter
{
  public static List<AppointmentSlotDto> Apply(
    IReadOnlyList<TimeOnly> rawSlots,
    IReadOnlyList<TimeRange> existingAppointments,
    int serviceDurationMinutes,
    int slotStepMinutes,
    GapFillingSettings? settings)
  {
    if (settings == null || settings.Mode == GapFillingMode.Disabled)
    {
      return rawSlots.Select(t => new AppointmentSlotDto(t.ToString("HH:mm"))).ToList();
    }

    var rawSlotsSet = new HashSet<TimeOnly>(rawSlots);
    var preferredTimes = ComputePreferredTimes(
      rawSlotsSet, existingAppointments, serviceDurationMinutes, slotStepMinutes, settings);

    if (settings.Mode == GapFillingMode.AdjacentOnly)
    {
      if (existingAppointments.Count == 0)
      {
        return rawSlots.Select(t => new AppointmentSlotDto(t.ToString("HH:mm"), false)).ToList();
      }
      return rawSlots
        .Where(t => preferredTimes.Contains(t))
        .Select(t => new AppointmentSlotDto(t.ToString("HH:mm"), true))
        .ToList();
    }

    // PreferAdjacent: return all slots, mark preferred ones
    return rawSlots
      .Select(t => new AppointmentSlotDto(t.ToString("HH:mm"), preferredTimes.Contains(t)))
      .ToList();
  }

  private static HashSet<TimeOnly> ComputePreferredTimes(
    HashSet<TimeOnly> rawSlotsSet,
    IReadOnlyList<TimeRange> existingAppointments,
    int serviceDurationMinutes,
    int slotStepMinutes,
    GapFillingSettings settings)
  {
    var preferred = new HashSet<TimeOnly>();
    if (rawSlotsSet.Count == 0 || existingAppointments.Count == 0)
    {
      return preferred;
    }

    var buffer = settings.BufferMinutes;
    var lookahead = settings.LookaheadSlots;
    var sortedSlots = rawSlotsSet.OrderBy(s => s).ToList();

    foreach (var appt in existingAppointments)
    {
      // "After": pierwszy slot z startem w oknie [appt.End + buffer, appt.End + buffer + slotStep).
      // Tolerancja jednego kroku gridu obsługuje wizyty, których koniec nie pada
      // na grid (np. usługa 40-min przy slotStep=15: wizyta 11:30–12:10, sąsiad 12:15).
      var afterMin = appt.EndTime.AddMinutes(buffer);
      var afterCap = appt.EndTime.AddMinutes(buffer + slotStepMinutes);
      TimeOnly? afterBase = null;
      foreach (var s in sortedSlots)
      {
        if (s >= afterCap) break;
        if (s >= afterMin) { afterBase = s; break; }
      }
      if (afterBase is { } afterStart)
      {
        for (int i = 0; i < lookahead; i++)
        {
          var candidate = afterStart.AddMinutes(i * slotStepMinutes);
          if (rawSlotsSet.Contains(candidate)) preferred.Add(candidate);
        }
      }

      // "Before": slot kończący się w oknie (appt.Start - buffer - slotStep, appt.Start - buffer],
      // czyli slot.Start ∈ (appt.Start - buffer - slotStep - duration, appt.Start - buffer - duration].
      var beforeMax = appt.StartTime.AddMinutes(-buffer - serviceDurationMinutes);
      var beforeFloor = appt.StartTime.AddMinutes(-buffer - serviceDurationMinutes - slotStepMinutes);
      TimeOnly? beforeBase = null;
      for (int i = sortedSlots.Count - 1; i >= 0; i--)
      {
        var s = sortedSlots[i];
        if (s <= beforeFloor) break;
        if (s <= beforeMax) { beforeBase = s; break; }
      }
      if (beforeBase is { } beforeStart)
      {
        for (int i = 0; i < lookahead; i++)
        {
          var candidate = beforeStart.AddMinutes(-i * slotStepMinutes);
          if (rawSlotsSet.Contains(candidate)) preferred.Add(candidate);
        }
      }
    }

    return preferred;
  }
}
