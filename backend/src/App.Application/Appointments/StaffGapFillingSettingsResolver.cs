using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Appointments;

/// <summary>
/// Reguły gap-filling dla panelu salonu (ręczne wizyty, dostępność miesiąca).
/// Publiczny booking ma osobną ścieżkę (<see cref="Booking.BookingAppointments.Queries.GetBookingMonthAvailabilityQuery"/>).
/// </summary>
internal static class StaffGapFillingSettingsResolver
{
  public static GapFillingSettings? Resolve(
    GapFillingSettings? tenantSettings,
    bool skipGapFilling = false,
    bool enforceStrictGapFilter = false)
  {
    if (skipGapFilling)
    {
      return null;
    }

    GapFillingSettings? effective;
    if (tenantSettings != null && tenantSettings.Mode != GapFillingMode.Disabled)
    {
      effective = tenantSettings;
    }
    else
    {
      effective = new GapFillingSettings(GapFillingMode.PreferAdjacent, 0, 1);
    }

    // AdjacentOnly jako twardy filtr tylko w publicznym bookingu (`enforceStrictGapFilter`).
    if (!enforceStrictGapFilter && effective.Mode == GapFillingMode.AdjacentOnly)
    {
      effective = new GapFillingSettings(
        GapFillingMode.PreferAdjacent,
        effective.BufferMinutes,
        effective.LookaheadSlots);
    }

    return effective;
  }
}
