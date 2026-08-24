using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

/// <summary>
/// EMP-002, EMP-003, EMP-005, EMP-007, EMP-012, EMP-013 — domenowe pokrycie
/// metod Update*, Deactivate, kalkulacji ceny/czasu, schedulingu i urlopów.
/// </summary>
public sealed class EmployeeDomainExtraTests
{
  // EMP-002: UpdatePersonalData / UpdateEmail
  [Fact]
  public void UpdatePersonalData_updates_first_last_and_specialization()
  {
    var emp = NewEmployee();

    emp.UpdatePersonalData("Anna", "Nowak", "Strzyżenie męskie");

    Assert.Equal("Anna", emp.FirstName);
    Assert.Equal("Nowak", emp.LastName);
    Assert.Equal("Strzyżenie męskie", emp.Specialization);
  }

  [Fact]
  public void UpdatePersonalData_with_whitespace_specialization_stores_null()
  {
    var emp = NewEmployee();

    emp.UpdatePersonalData("Anna", "Nowak", "   ");

    Assert.Null(emp.Specialization);
  }

  [Fact]
  public void UpdateEmail_normalises_email_lowercase_and_trim()
  {
    var emp = NewEmployee();

    emp.UpdateEmail("  Ann.Smith@Example.COM  ");

    Assert.Equal("ann.smith@example.com", emp.Email);
  }

  // EMP-003: Deactivate
  [Fact]
  public void Deactivate_sets_IsActive_false()
  {
    var emp = NewEmployee();
    Assert.True(emp.IsActive);

    emp.Deactivate();

    Assert.False(emp.IsActive);
  }

  // EMP-005: CalculateEndTime / CalculateTotalPrice
  [Fact]
  public void CalculateEndTime_uses_custom_duration_when_service_assigned()
  {
    var tenantId = Guid.NewGuid();
    var emp = new Employee(tenantId, null, "A", "B", "a@b.co");
    var serviceId = Guid.NewGuid();
    emp.AssignService(tenantId, serviceId, customDuration: 45, customPrice: new Money(100m, "PLN"));

    var end = emp.CalculateEndTime(new TimeOnly(10, 0), serviceId, defaultDuration: 30);

    Assert.Equal(new TimeOnly(10, 45), end);
  }

  [Fact]
  public void CalculateEndTime_falls_back_to_default_duration_when_service_not_assigned()
  {
    var emp = NewEmployee();

    var end = emp.CalculateEndTime(new TimeOnly(10, 0), Guid.NewGuid(), defaultDuration: 30);

    Assert.Equal(new TimeOnly(10, 30), end);
  }

  [Fact]
  public void CalculateTotalPrice_returns_custom_price_when_set()
  {
    var tenantId = Guid.NewGuid();
    var emp = new Employee(tenantId, null, "A", "B", "a@b.co");
    var serviceId = Guid.NewGuid();
    var customPrice = new Money(123m, "PLN");
    emp.AssignService(tenantId, serviceId, 30, customPrice);

    var total = emp.CalculateTotalPrice(serviceId, new Money(80m, "PLN"));

    Assert.Equal(customPrice, total);
  }

  [Fact]
  public void CalculateTotalPrice_throws_EmployeeServiceMissing_when_service_not_assigned()
  {
    var emp = NewEmployee();

    Assert.Throws<EmployeeServiceMissingException>(() =>
      emp.CalculateTotalPrice(Guid.NewGuid(), new Money(80m, "PLN")));
  }

  // EMP-007: Schedule guards
  [Fact]
  public void AddSchedule_with_overlapping_range_throws_SchedulesCollision()
  {
    var emp = NewEmployee();
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var scheduleDays = new[] { new ScheduleDay(workRanges, breaks: null, cycleIndex: 1) };
    emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), 1, scheduleDays);

    Assert.Throws<SchedulesCollisionException>(() =>
      emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)), 1, scheduleDays));
  }

  // EMP-014: nieaktywny grafik może dowolnie nachodzić na aktywny (scenariusz „szkic / podmiana")
  [Fact]
  public void AddSchedule_inactive_overlapping_active_does_not_throw()
  {
    var emp = NewEmployee();
    var days = OneDay(cycleIndex: 1);
    emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), 1, days);

    // Nieaktywny szkic nachodzący na aktywny grafik — dozwolony.
    var ex = Record.Exception(() =>
      emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)), 1, days, isActive: false));

    Assert.Null(ex);
  }

  // EMP-015: nie da się WŁĄCZYĆ grafiku kolidującego z aktywnym
  [Fact]
  public void SetScheduleActive_activating_overlapping_active_throws()
  {
    var emp = NewEmployee();
    var days = OneDay(cycleIndex: 1);
    emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), 1, days);
    var draftId = emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)), 1, days, isActive: false);

    Assert.Throws<SchedulesCollisionException>(() => emp.SetScheduleActive(draftId, isActive: true));
  }

  // EMP-016: podmiana — wyłącz stary aktywny, włącz nachodzący szkic → OK
  [Fact]
  public void SetScheduleActive_swap_deactivate_then_activate_overlapping_succeeds()
  {
    var emp = NewEmployee();
    var days = OneDay(cycleIndex: 1);
    var oldId = emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), 1, days);
    var draftId = emp.AddSchedule(new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)), 1, days, isActive: false);

    emp.SetScheduleActive(oldId, isActive: false);
    var ex = Record.Exception(() => emp.SetScheduleActive(draftId, isActive: true));

    Assert.Null(ex);
    Assert.True(emp.Schedules.Single(s => s.Id == draftId).IsActive);
    Assert.False(emp.Schedules.Single(s => s.Id == oldId).IsActive);
  }

  // EMP-017: nieaktywny grafik nie generuje dostępności (ignorowany w ResolveScheduleDay)
  [Fact]
  public void IsAvailable_ignores_inactive_schedule()
  {
    var monday = new DateOnly(2026, 7, 6); // poniedziałek
    var range = new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

    var active = NewEmployee();
    active.AddSchedule(range, 1, OneDay(cycleIndex: (int)DayOfWeek.Monday), isActive: true);
    Assert.True(active.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), monday));

    var inactive = NewEmployee();
    inactive.AddSchedule(range, 1, OneDay(cycleIndex: (int)DayOfWeek.Monday), isActive: false);
    Assert.False(inactive.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), monday));
  }

  private static ScheduleDay[] OneDay(int cycleIndex)
  {
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    return new[] { new ScheduleDay(workRanges, breaks: null, cycleIndex) };
  }

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  public void EmployeeSchedule_rejects_numberOfCycles_outside_1_to_4(int numberOfCycles)
  {
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var days = new[] { new ScheduleDay(workRanges, breaks: null, cycleIndex: 1) };

    Assert.ThrowsAny<Exception>(() =>
      new EmployeeSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), numberOfCycles, days));
  }

  [Fact]
  public void EmployeeSchedule_rejects_duplicate_CycleIndex()
  {
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var days = new[]
    {
      new ScheduleDay(workRanges, breaks: null, cycleIndex: 1),
      new ScheduleDay(workRanges, breaks: null, cycleIndex: 1),
    };

    Assert.Throws<ScheduleDaysCollisionException>(() =>
      new EmployeeSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days));
  }

  [Fact]
  public void EmployeeSchedule_rejects_ScheduleDays_count_exceeding_cycles_times_seven()
  {
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    // numberOfCycles=1 → max 7 days; 8 days breach
    var days = Enumerable.Range(0, 8)
      .Select(i => new ScheduleDay(workRanges, breaks: null, cycleIndex: i))
      .ToArray();

    Assert.Throws<InvalidScheduleDaysCountException>(() =>
      new EmployeeSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, days));
  }

  // EMP-012: ResolveScheduleDay via IsAvailable (private method, testowane przez efekt)
  [Fact]
  public void IsAvailable_uses_correct_day_in_simple_weekly_cycle()
  {
    var emp = NewEmployee();
    var monday = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    emp.SetWeeklySchedule(new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
      [DayOfWeek.Monday] = monday,
    });

    var nextMonday = NextDayOfWeek(DayOfWeek.Monday);
    Assert.True(emp.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), nextMonday));

    var nextTuesday = nextMonday.AddDays(1);
    Assert.False(emp.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), nextTuesday));
  }

  // EMP-013: IsAvailable returns false during active leave
  [Fact]
  public void IsAvailable_returns_false_when_date_is_within_active_leave()
  {
    var emp = NewEmployee();
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => workRanges);
    emp.SetWeeklySchedule(weekly);

    var start = new DateOnly(2026, 7, 1);
    var end = new DateOnly(2026, 7, 7);
    emp.AddLeave(start, end);

    Assert.False(emp.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), new DateOnly(2026, 7, 3)));
    Assert.True(emp.IsAvailable(new TimeOnly(10, 0), new TimeOnly(11, 0), new DateOnly(2026, 7, 8)));
  }

  [Fact]
  public void GetWorkingRanges_returns_empty_when_date_within_active_leave()
  {
    var emp = NewEmployee();
    var workRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(16, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => workRanges);
    emp.SetWeeklySchedule(weekly);

    emp.AddLeave(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7));

    var ranges = emp.GetWorkingRanges(new DateOnly(2026, 7, 3));

    Assert.Empty(ranges);
  }

  // EMP-009 Negative: AddLeave overlap (domain)
  [Fact]
  public void AddLeave_with_overlap_throws_LeaveOverlapException()
  {
    var emp = NewEmployee();
    emp.AddLeave(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));

    Assert.Throws<LeaveOverlapException>(() =>
      emp.AddLeave(new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 10)));
  }

  private static Employee NewEmployee() =>
    new(Guid.NewGuid(), null, "Test", "User", "test@e.local");

  private static DateOnly NextDayOfWeek(DayOfWeek target)
  {
    var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
    while (d.DayOfWeek != target)
    {
      d = d.AddDays(1);
    }
    return d;
  }
}
