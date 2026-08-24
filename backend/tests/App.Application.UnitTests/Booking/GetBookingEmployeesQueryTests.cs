using App.Application.Booking.BookingEmployees.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// GetBookingEmployeesQuery — intersekcja: dla combo zwracani są tylko pracownicy oferujący
/// WSZYSTKIE wybrane usługi (combo realizuje jeden pracownik).
/// </summary>
public sealed class GetBookingEmployeesQueryTests
{
  [Fact]
  public async Task Returns_only_employees_offering_ALL_requested_services()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, svc1, svc2, anna, bartek) = Setup();
    // anna oferuje svc1 + svc2; bartek tylko svc1.
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var both = await handler.Handle(new GetBookingEmployeesQuery(new[] { svc1, svc2 }), ct);

    Assert.Single(both);
    Assert.Equal(anna, both[0].Id);
  }

  [Fact]
  public async Task Single_service_returns_all_employees_offering_it()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, svc1, _, anna, bartek) = Setup();
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(new[] { svc1 }), ct);

    Assert.Equal(2, result.Count);
    Assert.Contains(result, e => e.Id == anna);
    Assert.Contains(result, e => e.Id == bartek);
  }

  [Fact]
  public async Task Empty_or_null_service_list_returns_all_bookable_employees()
  {
    // Lejek klienta zaczyna od wyboru pracownika (przed usługą) → pusta lista usług = wszyscy bookowalni.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _, anna, bartek) = Setup();
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var empty = await handler.Handle(new GetBookingEmployeesQuery(System.Array.Empty<System.Guid>()), ct);
    var @null = await handler.Handle(new GetBookingEmployeesQuery(null), ct);

    Assert.Equal(2, empty.Count);
    Assert.Contains(empty, e => e.Id == anna);
    Assert.Contains(empty, e => e.Id == bartek);
    Assert.Equal(2, @null.Count);
  }

  [Fact]
  public async Task Empty_service_list_excludes_non_bookable_employees()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _, anna, bartek) = Setup();
    // Kiosk / niebookowalny pracownik nie może zostać wybrany w lejku.
    var kiosk = new Employee(tenantId, null, "Kiosk", "Recepcja", "kiosk@salon.local");
    kiosk.MakeNonBookable();
    db.Employees.Add(kiosk);
    db.SaveChanges();
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(null), ct);

    Assert.Equal(2, result.Count);
    Assert.DoesNotContain(result, e => e.Id == kiosk.Id);
  }

  [Fact]
  public async Task Duplicate_service_ids_do_not_break_intersection()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, svc1, _, anna, bartek) = Setup();
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(new[] { svc1, svc1 }), ct);

    Assert.Equal(2, result.Count); // duplikat svc1 = wciąż „oferuje svc1" → oboje
  }

  [Fact]
  public async Task Flags_employee_without_any_working_day_as_having_no_upcoming_schedule()
  {
    // Publiczny kreator blokuje kafelek pracownika bez grafiku, zamiast wpuszczać klientkę
    // w pusty kalendarz. Anna pracuje pon–pt, Bartek nie ma grafiku w ogóle.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _, anna, bartek) = Setup();
    GiveWeekdaySchedule(db, anna);
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(null), ct);

    Assert.True(result.Single(e => e.Id == anna).HasUpcomingSchedule);
    Assert.False(result.Single(e => e.Id == bartek).HasUpcomingSchedule);
  }

  [Fact]
  public async Task Leave_covering_the_whole_window_leaves_employee_without_upcoming_schedule()
  {
    // Grafik jest, ale urlop przykrywa całe okno rezerwacji (dziś → koniec miesiąca +3).
    // Z punktu widzenia klientki to ten sam przypadek co brak grafiku.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _, anna, _) = Setup();
    GiveWeekdaySchedule(db, anna);
    var employee = db.Employees.Single(e => e.Id == anna);
    var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);
    employee.AddLeave(today, today.AddMonths(5));
    db.SaveChanges();
    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(null), ct);

    Assert.False(result.Single(e => e.Id == anna).HasUpcomingSchedule);
  }

  [Fact]
  public async Task Schedule_starting_later_inside_the_window_still_flags_upcoming_schedule()
  {
    // Regresja pod optymalizację skanu odznaki: pętla dnia po dniu pomija pracowników BEZ grafiku
    // w oknie, żeby nie przemiatać całego horyzontu. Warunek pominięcia musi patrzeć na PRZECIĘCIE
    // zakresu obowiązywania z oknem, nie na „grafik zaczyna się dziś" — inaczej nowa pracownica
    // startująca za dwa miesiące cicho traci odznakę.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId, _, _, anna, _) = Setup();

    var employee = db.Employees.Single(e => e.Id == anna);
    var start = System.DateOnly.FromDateTime(System.DateTime.UtcNow).AddDays(60);
    var ranges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new System.TimeOnly(8, 0), new System.TimeOnly(16, 0)),
    };
    // Grafik obowiązujący dopiero od +60 dni, na każdy dzień tygodnia (cycleIndex = DayOfWeek).
    employee.AddSchedule(
      new DateRange(start, start.AddDays(90)),
      1,
      System.Linq.Enumerable.Range(0, 7)
        .Select(i => new ScheduleDay(
          ranges.Select(r => new TimeRange(r.StartTime, r.EndTime)).ToList(), null, cycleIndex: i))
        .ToList());
    db.SaveChanges();

    var handler = new GetBookingEmployeesQueryHandler(db, new EmployeeRepository(db), new FakeCurrentTenantService(tenantId));

    var result = await handler.Handle(new GetBookingEmployeesQuery(null), ct);

    Assert.True(result.Single(e => e.Id == anna).HasUpcomingSchedule);
  }

  private static void GiveWeekdaySchedule(ApplicationDbContext db, System.Guid employeeId)
  {
    var employee = db.Employees.Single(e => e.Id == employeeId);
    var ranges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new System.TimeOnly(8, 0), new System.TimeOnly(16, 0)),
    };
    employee.SetWeeklySchedule(new Dictionary<System.DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
      [System.DayOfWeek.Monday] = ranges,
      [System.DayOfWeek.Tuesday] = ranges,
      [System.DayOfWeek.Wednesday] = ranges,
      [System.DayOfWeek.Thursday] = ranges,
      [System.DayOfWeek.Friday] = ranges,
      [System.DayOfWeek.Saturday] = ranges,
      [System.DayOfWeek.Sunday] = ranges,
    });
    db.SaveChanges();
  }

  private static (ApplicationDbContext db, System.Guid tenantId, System.Guid svc1, System.Guid svc2, System.Guid anna, System.Guid bartek) Setup()
  {
    var tenantId = System.Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var vat = new VatRate(tenantId, "VAT", 0.23m);
    var s1 = new Service(tenantId, null, vat.Id, "Przedłużanie", new Money(120m, "PLN"), 90);
    var s2 = new Service(tenantId, null, vat.Id, "Pedicure", new Money(100m, "PLN"), 60);

    var anna = new Employee(tenantId, null, "Anna", "Kowalska", "anna@salon.local");
    anna.AssignService(tenantId, s1.Id, s1.DurationInMinutes, s1.Price);
    anna.AssignService(tenantId, s2.Id, s2.DurationInMinutes, s2.Price);

    var bartek = new Employee(tenantId, null, "Bartek", "Nowak", "bartek@salon.local");
    bartek.AssignService(tenantId, s1.Id, s1.DurationInMinutes, s1.Price);

    db.VatRates.Add(vat);
    db.Services.AddRange(s1, s2);
    db.Employees.AddRange(anna, bartek);
    db.SaveChanges();

    return (db, tenantId, s1.Id, s2.Id, anna.Id, bartek.Id);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public System.Guid? TenantId { get; }
    public FakeCurrentTenantService(System.Guid tenantId) => TenantId = tenantId;
  }
}
