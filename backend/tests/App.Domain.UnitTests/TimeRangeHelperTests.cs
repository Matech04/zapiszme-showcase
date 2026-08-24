using App.Domain.Common;
using Xunit;

namespace App.Domain.UnitTests;

public class TimeRangeHelperTests
{
  [Fact]
  public void CropTimeRange_ShouldSplitSlot_WhenAppointmentIsInMiddle()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(10, 0)) };
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(8, 30), new TimeOnly(9, 0)) };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Equal(2, result.Count);
    Assert.Equal(new TimeOnly(8, 0), result[0].StartTime);
    Assert.Equal(new TimeOnly(8, 30), result[0].EndTime);
    Assert.Equal(new TimeOnly(9, 0), result[1].StartTime);
    Assert.Equal(new TimeOnly(10, 0), result[1].EndTime);
  }

  [Fact]
  public void CropTimeRange_ShouldCropStart_WhenAppointmentOverlapsBeginning()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(12, 0)) };
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(10, 0)) };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Single(result);
    Assert.Equal(new TimeOnly(10, 0), result[0].StartTime);
    Assert.Equal(new TimeOnly(12, 0), result[0].EndTime);
  }

  [Fact]
  public void CropTimeRange_ShouldCropEnd_WhenAppointmentOverlapsEnd()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(12, 0)) };
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(11, 0), new TimeOnly(13, 0)) };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Single(result);
    Assert.Equal(new TimeOnly(9, 0), result[0].StartTime);
    Assert.Equal(new TimeOnly(11, 0), result[0].EndTime);
  }

  [Fact]
  public void CropTimeRange_ShouldReturnEmpty_WhenAppointmentCoversFullSlot()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(10, 0), new TimeOnly(11, 0)) };
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(12, 0)) };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Empty(result);
  }

  [Fact]
  public void CropTimeRange_ShouldHandleMultipleAppointments()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(16, 0)) };
    var appointments = new List<TimeRange>
        {
            new TimeRange(new TimeOnly(9, 0), new TimeOnly(10, 0)),
            new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))
        };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Equal(3, result.Count);
    // 08:00 - 09:00
    Assert.Equal(new TimeOnly(8, 0), result[0].StartTime);
    Assert.Equal(new TimeOnly(9, 0), result[0].EndTime);
    // 10:00 - 12:00
    Assert.Equal(new TimeOnly(10, 0), result[1].StartTime);
    Assert.Equal(new TimeOnly(12, 0), result[1].EndTime);
    // 13:00 - 16:00
    Assert.Equal(new TimeOnly(13, 0), result[2].StartTime);
    Assert.Equal(new TimeOnly(16, 0), result[2].EndTime);
  }

  [Fact]
  public void CropTimeRange_ShouldFilterOutShortSlots()
  {
    // Arrange
    var availableRange = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(10, 0)) };
    // Appointment leaves only 10 minutes at the beginning (not enough for the 15min filter)
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(8, 10), new TimeOnly(10, 0)) };

    // Act
    var result = TimeRangeHelper.CropTimeRange(availableRange, appointments);

    // Assert
    Assert.Empty(result);
  }
}
