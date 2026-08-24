namespace App.Domain.Common;

public static class TimeRangeHelper
{
  public static List<TimeRange> CropTimeRange(
    List<TimeRange> AvailableSlots,
    List<TimeRange> UnavailableSlots,
    int minRemainingMinutes = 15)
  {
    AvailableSlots = AvailableSlots.OrderBy(s => s.StartTime).ToList();
    UnavailableSlots = UnavailableSlots.OrderBy(s => s.StartTime).ToList();

    var result = new List<TimeRange>(AvailableSlots);


    foreach (TimeRange breakSlot in UnavailableSlots)
    {

      var croppedList = new List<TimeRange>();

      foreach (TimeRange slot in result)
      {

        if (slot.StartTime >= breakSlot.EndTime || slot.EndTime <= breakSlot.StartTime)
        {
          croppedList.Add(slot);
          continue;
        }

        if (breakSlot.StartTime > slot.StartTime)
        {
          croppedList.Add(new TimeRange(slot.StartTime, breakSlot.StartTime));
        }
        if (slot.EndTime > breakSlot.EndTime)
        {
          croppedList.Add(new TimeRange(breakSlot.EndTime, slot.EndTime));
        }

      }

      result = croppedList;
    }
    return result
      .Where(slot => (slot.EndTime.ToTimeSpan() - slot.StartTime.ToTimeSpan()).TotalMinutes >= minRemainingMinutes)
      .ToList();
  }

}