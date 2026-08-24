using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// `GET /api/notifications/outside-schedule` — endpoint nie miał ŻADNEGO pokrycia, a jest pollowany
/// przez panel co 8 s.
///
/// Testy powstały przy naprawie znaleziska CRITICAL z preflightu 2026-07-31: handler materializował
/// encję `Employee` (10 kolekcji owned → iloczyn kartezjański, bez `AsNoTracking`), więc źródło
/// danych zostało podmienione na `IEmployeeRepository.GetManyForAvailabilityAsync`.
///
/// Ta podmiana NIE jest neutralna semantycznie: repozytorium zawęża `Overrides`, `Leaves`
/// i `MonthPublications` do okna [from, to], podczas gdy poprzednie zapytanie ładowało całą historię
/// pracownika. Poniższe testy pilnują, że zawężenie nie zmienia wyniku — bo `IsAvailable` pytamy
/// wyłącznie o daty z tego samego okna.
/// </summary>
public class OutsideScheduleNotificationsIntegrationTests
{
  // Seed daje pracownikowi grafik Pn–Pt 9:00–17:00 (BuildTenantAggregate).
  private static readonly DateOnly Monday = new(2026, 9, 14);

  [Fact]
  public async Task Appointment_inside_working_hours_is_not_reported()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    await AddAppointmentAsync(factory, seed, Monday, new TimeOnly(10, 0), new TimeOnly(11, 0), ct);

    var dtos = await FetchAsync(factory, Monday, Monday, ct);

    Assert.Empty(dtos);
  }

  [Fact]
  public async Task Appointment_outside_working_hours_is_reported()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // 5:00 — grubo przed 9:00, czyli poza oknem pracy.
    var id = await AddAppointmentAsync(factory, seed, Monday, new TimeOnly(5, 0), new TimeOnly(6, 0), ct);

    var dtos = await FetchAsync(factory, Monday, Monday, ct);

    var dto = Assert.Single(dtos);
    Assert.Equal(id, dto.AppointmentId);
    Assert.Equal(seed.EmployeeId, dto.EmployeeId);
  }

  /// <summary>
  /// Regresja właściwa dla podmiany źródła danych: `GetManyForAvailabilityAsync` filtruje `Leaves`
  /// po przecięciu z oknem [from, to]. Gdyby filtr był zbyt wąski (np. wymagał zawierania się urlopu
  /// w oknie), wizyta w dniu urlopu przestałaby być raportowana i panel przemilczałby konflikt.
  /// Urlop celowo WYKRACZA poza pytane okno z obu stron.
  /// </summary>
  [Fact]
  public async Task Appointment_during_leave_spanning_beyond_window_is_reported()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var employee = await db.Employees
        .IgnoreQueryFilters()
        .Include(e => e.Leaves)
        .FirstAsync(e => e.Id == seed.EmployeeId, ct);

      employee.AddLeave(Monday.AddDays(-3), Monday.AddDays(3));
      await db.SaveChangesAsync(ct);
    }

    // Wizyta w godzinach pracy, ale w dniu urlopu → konflikt.
    await AddAppointmentAsync(factory, seed, Monday, new TimeOnly(10, 0), new TimeOnly(11, 0), ct);

    var dtos = await FetchAsync(factory, Monday, Monday, ct);

    Assert.Single(dtos);
  }

  private static async Task<Guid> AddAppointmentAsync(
    BookingApiApplicationFactory factory,
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly start,
    TimeOnly end,
    CancellationToken ct)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var appointment = new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      seed.CustomerId,
      date,
      start,
      end,
      AppointmentStatus.Booked,
      new Money(100m, "PLN"),
      string.Empty,
      lease: null);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);
    return appointment.Id;
  }

  private static async Task<List<OutsideScheduleRow>> FetchAsync(
    BookingApiApplicationFactory factory, DateOnly from, DateOnly to, CancellationToken ct)
  {
    var client = factory.CreateOwnerClient();
    var response = await client.GetAsync(
      $"/api/notifications/outside-schedule?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return await response.Content.ReadFromJsonAsync<List<OutsideScheduleRow>>(ct) ?? [];
  }

  private sealed record OutsideScheduleRow(Guid AppointmentId, Guid EmployeeId);
}
