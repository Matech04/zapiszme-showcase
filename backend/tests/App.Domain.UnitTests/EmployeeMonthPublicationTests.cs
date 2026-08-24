using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

namespace App.Domain.UnitTests;

/// <summary>
/// Widoczność terminów w PUBLICZNYM bookingu — oś niezależna od tego, czy dostępność bierze się
/// z grafiku powtarzalnego, czy z samych dni specjalnych. Domyślnie rządzi horyzont kroczący
/// (Tenant.BookingHorizonDays); jawny wiersz miesiąca nadpisuje go w obie strony.
/// </summary>
public class EmployeeMonthPublicationTests
{
  private static TimeOnly T(int h, int m = 0) => new(h, m);

  private static readonly DateOnly Today = new(2026, 7, 22);
  private const int Horizon = 90; // → 2026-10-20

  private static readonly DateOnly InsideHorizon = new(2026, 9, 10);   // wrzesień, mieści się
  private static readonly DateOnly BeyondHorizon = new(2026, 12, 15);  // grudzień, poza horyzontem

  private static Employee NewEmployee() =>
    new(Guid.NewGuid(), Guid.NewGuid(), "Ala", "Testowa", "ala@test.local");

  [Fact]
  public void Without_row_date_inside_horizon_is_open()
  {
    var e = NewEmployee();

    Assert.True(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));
  }

  [Fact]
  public void Without_row_date_beyond_horizon_is_closed()
  {
    var e = NewEmployee();

    Assert.False(e.IsDateOpenForOnlineBooking(BeyondHorizon, Today, Horizon));
  }

  [Fact]
  public void Horizon_boundary_is_inclusive()
  {
    var e = NewEmployee();
    var lastOpenDay = Today.AddDays(Horizon);

    Assert.True(e.IsDateOpenForOnlineBooking(lastOpenDay, Today, Horizon));
    Assert.False(e.IsDateOpenForOnlineBooking(lastOpenDay.AddDays(1), Today, Horizon));
  }

  [Fact]
  public void Row_with_future_opening_closes_month_that_would_fit_in_horizon()
  {
    // Sedno prośby klientki: wrzesień jest wpisany i mieści się w horyzoncie,
    // ale klient ma go zobaczyć dopiero 1 września.
    var e = NewEmployee();
    e.SetMonthPublication(2026, 9, new DateOnly(2026, 9, 1));

    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));
  }

  [Fact]
  public void Row_opens_itself_on_the_given_day_without_anyone_clicking()
  {
    var e = NewEmployee();
    var opensOn = new DateOnly(2026, 9, 1);
    e.SetMonthPublication(2026, 9, opensOn);

    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, opensOn.AddDays(-1), Horizon));
    Assert.True(e.IsDateOpenForOnlineBooking(InsideHorizon, opensOn, Horizon));
    Assert.True(e.IsDateOpenForOnlineBooking(InsideHorizon, opensOn.AddDays(1), Horizon));
  }

  [Fact]
  public void Row_without_opening_date_is_closed_indefinitely()
  {
    var e = NewEmployee();
    e.SetMonthPublication(2026, 9, null);

    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));
    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, new DateOnly(2030, 1, 1), Horizon));
  }

  [Fact]
  public void Row_can_open_a_month_that_lies_beyond_the_horizon()
  {
    // „Otwieramy grudzień już teraz, bo święta" — wiersz nadpisuje horyzont także w górę.
    var e = NewEmployee();
    e.SetMonthPublication(2026, 12, Today);

    Assert.True(e.IsDateOpenForOnlineBooking(BeyondHorizon, Today, Horizon));
  }

  [Fact]
  public void Row_applies_to_whole_month_only()
  {
    var e = NewEmployee();
    e.SetMonthPublication(2026, 9, null); // wrzesień zamknięty

    Assert.False(e.IsDateOpenForOnlineBooking(new DateOnly(2026, 9, 30), Today, Horizon));
    Assert.True(e.IsDateOpenForOnlineBooking(new DateOnly(2026, 10, 1), Today, Horizon));
  }

  [Fact]
  public void Setting_same_month_twice_updates_instead_of_duplicating()
  {
    var e = NewEmployee();
    e.SetMonthPublication(2026, 9, new DateOnly(2026, 9, 1));
    e.SetMonthPublication(2026, 9, new DateOnly(2026, 8, 15));

    var row = Assert.Single(e.MonthPublications);
    Assert.Equal(new DateOnly(2026, 8, 15), row.OpensOn);
  }

  [Fact]
  public void Clearing_returns_month_to_horizon_rule_not_to_closed()
  {
    // Różnica, która łatwo umyka: skasowanie wiersza NIE zamyka miesiąca — przywraca domyślną regułę.
    var e = NewEmployee();
    e.SetMonthPublication(2026, 9, null);
    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));

    e.ClearMonthPublication(2026, 9);

    Assert.Empty(e.MonthPublications);
    Assert.True(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));
  }

  [Fact]
  public void Clearing_a_month_that_has_no_row_is_a_no_op()
  {
    var e = NewEmployee();
    e.ClearMonthPublication(2026, 9);

    Assert.Empty(e.MonthPublications);
  }

  // Scenariusz „papierowego kalendarza" — pracownica bez grafiku powtarzalnego, sam dzień specjalny.
  // To zaprojektowany sposób pracy (patrz komentarz przy Employee.GetSlotModeForDate), więc publikacja
  // musi go obsługiwać tak samo jak grafik cykliczny.
  [Fact]
  public void Works_for_employee_with_only_special_days_and_no_recurring_schedule()
  {
    var e = NewEmployee();
    e.SetScheduleOverride(InsideHorizon, new ScheduleDay(new[] { new TimeRange(T(10), T(16)) }, null));
    e.SetMonthPublication(2026, 9, new DateOnly(2026, 9, 1));

    Assert.Empty(e.Schedules);
    Assert.False(e.IsDateOpenForOnlineBooking(InsideHorizon, Today, Horizon));

    // ...a po otwarciu ten sam dzień jest widoczny.
    Assert.True(e.IsDateOpenForOnlineBooking(InsideHorizon, new DateOnly(2026, 9, 1), Horizon));
  }

  [Fact]
  public void Publication_does_not_leak_into_panel_availability()
  {
    // Kluczowa granica: publikacja dotyczy WYŁĄCZNIE bookingu online. Panel musi dalej widzieć
    // godziny pracy i pozwalać personelowi wpisać wizytę w zamkniętym miesiącu.
    var e = NewEmployee();
    e.SetScheduleOverride(InsideHorizon, new ScheduleDay(new[] { new TimeRange(T(10), T(16)) }, null));
    e.SetMonthPublication(2026, 9, null); // zamknięte bezterminowo

    var ranges = Assert.Single(e.GetWorkingRanges(InsideHorizon));
    Assert.Equal(T(10), ranges.StartTime);
    Assert.Equal(T(16), ranges.EndTime);
    Assert.True(e.IsAvailable(new TimeRange(T(11), T(12)), InsideHorizon));
  }

  [Theory]
  [InlineData(2026, 0)]
  [InlineData(2026, 13)]
  [InlineData(1999, 6)]
  public void Invalid_year_or_month_is_rejected(int year, int month)
  {
    var e = NewEmployee();

    Assert.Throws<ArgumentException>(() => e.SetMonthPublication(year, month, null));
  }
}
