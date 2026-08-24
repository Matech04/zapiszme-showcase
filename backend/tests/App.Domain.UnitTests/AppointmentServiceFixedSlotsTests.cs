using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Services;
using Moq;

namespace App.Domain.UnitTests;

/// <summary>
/// Tryb stałych slotów: <see cref="AppointmentService.EmployeeFixedSlotsList"/> oferuje wpisaną
/// godzinę dopóki [start, start+usługa] nie nachodzi na istniejącą wizytę. Brak okna pracy / siatki.
/// </summary>
public class AppointmentServiceFixedSlotsTests
{
  private readonly AppointmentService _sut = new(new Mock<IAppointmentRepository>().Object);

  private static Employee CreateEmployee()
    => new(Guid.NewGuid(), Guid.NewGuid(), "Ala", "Nails", "ala@nails.local");

  private static TimeOnly T(int h, int m = 0) => new(h, m);

  [Fact]
  public void AllFixedTimes_returned_when_no_appointments()
  {
    var employee = CreateEmployee();
    var fixedTimes = new[] { T(9), T(12), T(15) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, new List<TimeRange>(), employee, 60);

    Assert.Equal(new[] { T(9), T(12), T(15) }, result);
  }

  [Fact]
  public void FixedTime_colliding_with_appointment_is_excluded()
  {
    var employee = CreateEmployee();
    var fixedTimes = new[] { T(9), T(12), T(15) };
    // Wizyta 9:00–10:00 koliduje z usługą 60 min startującą o 9:00.
    var appointments = new List<TimeRange> { new(T(9), T(10)) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, appointments, employee, 60);

    Assert.DoesNotContain(T(9), result);
    Assert.Contains(T(12), result);
    Assert.Contains(T(15), result);
  }

  [Fact]
  public void Overlapping_fixed_times_booking_one_consumes_the_other()
  {
    var employee = CreateEmployee();
    // Sloty 9:00 i 9:30, usługa 60 min. Po zarezerwowaniu 9:00–10:00 slot 9:30 koliduje.
    var fixedTimes = new[] { T(9), T(9, 30) };
    var appointments = new List<TimeRange> { new(T(9), T(10)) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, appointments, employee, 60);

    Assert.DoesNotContain(T(9), result);
    Assert.DoesNotContain(T(9, 30), result);
  }

  [Fact]
  public void Non_overlapping_appointment_does_not_exclude_slot()
  {
    var employee = CreateEmployee();
    var fixedTimes = new[] { T(9), T(12) };
    // Wizyta 10:00–11:00 nie koliduje z usługą 60 min o 9:00 (kończy 10:00 — styk, bez nakładania).
    var appointments = new List<TimeRange> { new(T(10), T(11)) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, appointments, employee, 60);

    Assert.Contains(T(9), result);
    Assert.Contains(T(12), result);
  }

  [Fact]
  public void Service_crossing_midnight_is_skipped()
  {
    var employee = CreateEmployee();
    // 23:30 + 60 min = 00:30 (zawinięcie przez północ) → slot niemożliwy w tym dniu.
    var fixedTimes = new[] { T(23, 30) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, new List<TimeRange>(), employee, 60);

    Assert.Empty(result);
  }

  [Fact]
  public void Duplicates_are_deduped_and_result_is_sorted()
  {
    var employee = CreateEmployee();
    var fixedTimes = new[] { T(12), T(9), T(9) };

    var result = _sut.EmployeeFixedSlotsList(fixedTimes, new List<TimeRange>(), employee, 30);

    Assert.Equal(new[] { T(9), T(12) }, result);
  }

  [Fact]
  public void Empty_fixed_times_returns_empty()
  {
    var employee = CreateEmployee();

    var result = _sut.EmployeeFixedSlotsList(Array.Empty<TimeOnly>(), new List<TimeRange>(), employee, 60);

    Assert.Empty(result);
  }
}
