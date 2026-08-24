using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

public class AppointmentTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var serviceId = Guid.NewGuid();
    var customerId = Guid.NewGuid();
    var date = new DateOnly(2026, 2, 10);
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);
    var status = AppointmentStatus.Booked;
    var totalPrice = new Money(100m, "PLN");
    var notes = "Test notes";

    // Act
    var appointment = new Appointment(tenantId, employeeId, serviceId, customerId, date, startTime, endTime, status, totalPrice, notes, null);

    // Assert
    Assert.NotEqual(Guid.Empty, appointment.Id);
    Assert.Equal(tenantId, appointment.TenantId);
    Assert.Equal(employeeId, appointment.EmployeeId);
    Assert.Equal(serviceId, appointment.ServiceId);
    Assert.Equal(customerId, appointment.CustomerId);
    Assert.Equal(date, appointment.Date);
    Assert.Equal(startTime, appointment.StartTime);
    Assert.Equal(endTime, appointment.EndTime);
    Assert.Equal(status, appointment.Status);
    Assert.Equal(totalPrice, appointment.TotalPrice);
    Assert.Equal(notes, appointment.AppointmentNotes);
  }

  [Fact]
  public void Constructor_WithInvalidTimeRange_ShouldThrowException()
  {
    // Arrange
    var startTime = new TimeOnly(11, 0);
    var endTime = new TimeOnly(10, 0); // End before start

    // Act & Assert
    Assert.ThrowsAny<Exception>(() => new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new DateOnly(2026, 2, 10), startTime, endTime,
        AppointmentStatus.Booked, new Money(100m, "PLN"), "", null));
  }

  [Fact]
  public void ChangeStatus_ShouldUpdateStatus()
  {
    // Arrange
    var appointment = CreateTestAppointment();
    var newStatus = AppointmentStatus.Completed;

    // Act
    appointment.ChangeStatus(newStatus);

    // Assert
    Assert.Equal(newStatus, appointment.Status);
  }

  [Fact]
  public void ChangeStatus_WhenCancelingCompletedAppointment_ShouldThrowException()
  {
    // Arrange
    var appointment = CreateTestAppointment();
    appointment.ChangeStatus(AppointmentStatus.Completed);

    // Act & Assert
    var exception = Assert.Throws<AppointmentBookingRuleException>(() => appointment.ChangeStatus(AppointmentStatus.Canceled));
    Assert.Equal(ErrorCodes.AppointmentCompletedCannotBeCanceled, exception.ErrorCode);
  }

  [Fact]
  public void Update_ShouldChangeTimeAndEmployee()
  {
    // Arrange
    var appointment = CreateTestAppointment();
    var newEmployeeId = Guid.NewGuid();
    var newDate = new DateOnly(2026, 3, 1);
    var newStart = new TimeOnly(14, 0);
    var newEnd = new TimeOnly(15, 0);
    var newPrice = new Money(150m, "PLN");

    // Act
    appointment.Update(newEmployeeId, appointment.ServiceId, newDate, newStart, newEnd, newPrice);

    // Assert
    Assert.Equal(newEmployeeId, appointment.EmployeeId);
    Assert.Equal(newDate, appointment.Date);
    Assert.Equal(newStart, appointment.StartTime);
    Assert.Equal(newEnd, appointment.EndTime);
    Assert.Equal(newPrice, appointment.TotalPrice);
  }

  [Fact]
  public void ChangeNotes_ShouldUpdateNotes()
  {
    // Arrange
    var appointment = CreateTestAppointment();
    var newNotes = "Updated customer notes";

    // Act
    appointment.ChangeNotes(newNotes);

    // Assert
    Assert.Equal(newNotes, appointment.AppointmentNotes);
  }

  [Fact]
  public void SetFinalPrice_OnCompletedAppointment_SetsFinalPrice()
  {
    // Cena końcowa wpisywana zwykle po wizycie, gdy jest już Completed (auto-domknięta po EndTime).
    var appointment = CreateTestAppointment();
    appointment.ChangeStatus(AppointmentStatus.Completed);

    appointment.SetFinalPrice(new Money(180m, "PLN"));

    Assert.NotNull(appointment.FinalPrice);
    Assert.Equal(180m, appointment.FinalPrice!.Amount);
  }

  [Fact]
  public void SetFinalPrice_DefaultsToNull()
  {
    var appointment = CreateTestAppointment();

    Assert.Null(appointment.FinalPrice);
  }

  [Fact]
  public void SetFinalPrice_OnCanceledAppointment_ShouldThrow()
  {
    var appointment = CreateTestAppointment();
    appointment.ChangeStatus(AppointmentStatus.Canceled);

    var exception = Assert.Throws<AppointmentBookingRuleException>(() => appointment.SetFinalPrice(new Money(180m, "PLN")));
    Assert.Equal(ErrorCodes.AppointmentFinalPriceInvalidStatus, exception.ErrorCode);
  }

  private Appointment CreateTestAppointment()
  {
    return new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new DateOnly(2026, 2, 10), new TimeOnly(10, 0), new TimeOnly(11, 0),
        AppointmentStatus.Pending, new Money(100m, "PLN"), "Original notes", null);
  }
}

public class EmployeeAvailabilityTests
{
  private Employee CreateEmployee()
  {
    return new Employee(Guid.NewGuid(), Guid.NewGuid(), "Bartek", "Barber", "bartek@barber.com");
  }

  [Fact]
  public void IsAvailable_WhenInWorkingHoursAndNoBreak_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var ranges = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(16, 0)) };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    var result = employee.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), date);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public void IsAvailable_WhenOutsideWorkingHours_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var ranges = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(16, 0)) };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    var result = employee.IsAvailable(new TimeOnly(17, 0), new TimeOnly(18, 0), date);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsAvailable_WhenExactlyOnBreak_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    // New model: Break is simply a gap between ranges
    var ranges = new List<TimeRange>
        {
            new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new TimeRange(new TimeOnly(13, 0), new TimeOnly(16, 0))
        };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    var result = employee.IsAvailable(new TimeOnly(12, 15), new TimeOnly(12, 45), date);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsAvailable_WhenOverlappingBreakStart_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var ranges = new List<TimeRange>
        {
            new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new TimeRange(new TimeOnly(13, 0), new TimeOnly(16, 0))
        };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    var result = employee.IsAvailable(new TimeOnly(11, 30), new TimeOnly(12, 30), date);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsAvailable_WhenOverlappingBreakEnd_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var ranges = new List<TimeRange>
        {
            new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new TimeRange(new TimeOnly(13, 0), new TimeOnly(16, 0))
        };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    var result = employee.IsAvailable(new TimeOnly(12, 30), new TimeOnly(13, 30), date);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsAvailable_WithScheduleOverride_ShouldRespectOverride()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    // Weekly: 8-16
    var ranges = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(16, 0)) };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });
    // Override: krótszy dzień pracy 14:00-15:00, więc slot 10:00-11:00 powinien być niedostępny
    employee.SetScheduleOverride(date, new List<TimeRange> { new(new TimeOnly(14, 0), new TimeOnly(15, 0)) });

    // Act
    var result = employee.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), date);

    // Assert
    Assert.False(result);
  }
  [Fact]
  public void IsAvailable_WhenAppointmentEndsExactlyWhenBreakStarts_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var ranges = new List<TimeRange>
    {
        new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
        new TimeRange(new TimeOnly(13, 0), new TimeOnly(16, 0))
    };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    // Booking 11:00 -> 12:00 (Touching the break start)
    var result = employee.IsAvailable(new TimeOnly(11, 0), new TimeOnly(12, 0), date);

    // Assert
    Assert.True(result, "Should allow appointment ending exactly when break starts");
  }

  [Fact]
  public void IsAvailable_WhenAppointmentStartsExactlyWhenBreakEnds_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var ranges = new List<TimeRange>
    {
        new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
        new TimeRange(new TimeOnly(13, 0), new TimeOnly(16, 0))
    };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    // Booking 13:00 -> 14:00 (Touching the break end)
    var result = employee.IsAvailable(new TimeOnly(13, 0), new TimeOnly(14, 0), date);

    // Assert
    Assert.True(result, "Should allow appointment starting exactly when break ends");
  }

  [Fact]
  public void IsAvailable_WithSplitShift_WhenBookingInGap_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    // Split shift: 8-12 AND 16-20
    var ranges = new List<TimeRange>
    {
        new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
        new TimeRange(new TimeOnly(16, 0), new TimeOnly(20, 0))
    };
    employee.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> { { DayOfWeek.Monday, ranges } });

    // Act
    // Booking 13:00 -> 14:00 (In the middle of the split)
    var result = employee.IsAvailable(new TimeOnly(13, 0), new TimeOnly(14, 0), date);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsAvailable_WhenNoScheduleDefinedForDay_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    // We intentionally do NOT call SetWeeklySchedule for Monday

    // Act
    var result = employee.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), date);

    // Assert
    Assert.False(result, "Should default to unavailable if no schedule exists");
  }
}
