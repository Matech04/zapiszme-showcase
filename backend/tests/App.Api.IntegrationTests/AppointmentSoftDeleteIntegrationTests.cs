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
/// APPT-015 — soft-delete pracownika/usługi/klienta nie zmienia istniejących wizyt.
/// APPT-016 — rezerwacja z soft-deletowaną usługą (bug FindAsync) i pracownikiem.
/// </summary>
public sealed class AppointmentSoftDeleteIntegrationTests
{
  // IT-APPT-008: APPT-015 — Employee soft-deleted: existing Booked appointments remain unchanged
  [Fact]
  public async Task Employee_deactivated_existing_booked_appointments_remain_unchanged()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var apptId = SeedAppointment(factory.Services, seed, TestDates.InDays(30), new TimeOnly(9, 0));

    DeactivateEmployee(factory.Services, seed.EmployeeId);

    var ct = TestContext.Current.CancellationToken;
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);

    Assert.Equal(AppointmentStatus.Booked, appt.Status);
    Assert.Equal(seed.EmployeeId, appt.EmployeeId);
    Assert.Equal(seed.ServiceId, appt.ServiceId);
  }

  // IT-APPT-009: APPT-015 — Service soft-deleted: existing appointments retain serviceId and price snapshot
  [Fact]
  public async Task Service_deactivated_existing_appointments_retain_service_id_and_price()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var apptId = SeedAppointment(factory.Services, seed, TestDates.InDays(31), new TimeOnly(10, 0));

    var originalPrice = GetAppointmentPrice(factory.Services, apptId);

    DeactivateService(factory.Services, seed.ServiceId);

    var ct = TestContext.Current.CancellationToken;
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);

    Assert.Equal(seed.ServiceId, appt.ServiceId);
    Assert.Equal(originalPrice, appt.TotalPrice.Amount);
  }

  // IT-APPT-010: APPT-015 — Customer soft-deleted: their appointment history retained with customerId intact
  [Fact]
  public async Task Customer_deleted_appointment_history_retains_customer_id()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var apptId = SeedAppointment(factory.Services, seed, TestDates.InDays(32), new TimeOnly(11, 0));

    DeactivateCustomer(factory.Services, seed.CustomerId);

    var ct = TestContext.Current.CancellationToken;
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);

    Assert.Equal(seed.CustomerId, appt.CustomerId);
  }

  // IT-APPT-011: APPT-016 — Book appointment with soft-deleted service returns 404
  // BUG: ServiceRepository.GetByIdAsync uses FindAsync which bypasses IsActive global filter.
  // Ten test wykrywa błąd — oczekujemy 404, aktualnie zwraca 200 (FindAsync ignoruje filtr).
  [Fact]
  public async Task Book_appointment_with_deactivated_service_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    DeactivateService(factory.Services, seed.ServiceId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(50),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    // Oczekujemy 404 — usługa jest zdeaktywowana.
    // Błąd: FindAsync w ServiceRepository pomija filtr IsActive, więc test może zwrócić inny status.
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // IT-APPT-011: APPT-016 — Book appointment with deactivated employee returns 404.
  // Uwaga: deaktywujemy DRUGIEGO employee (nie tego od Ownera), bo TenantIdentifierMiddleware
  // resolvuje tenanta z Employee.UserId == caller && IsActive — gdy deaktywujemy Ownera,
  // sam caller traci uwierzytelnienie i dostaje 401/400 zamiast 404.
  [Fact]
  public async Task Book_appointment_with_deactivated_employee_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var targetEmployeeId = SeedSecondEmployee(factory.Services, seed.TenantId, seed.ServiceId);
    DeactivateEmployee(factory.Services, targetEmployeeId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = targetEmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(51),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // IT-APPT-012: BUG — soft-deleted service must NOT make existing appointments disappear
  // from the calendar (GetAppointmentsByRange) nor 404 the detail (GetAppointmentById).
  // Root cause: INNER JOIN na _context.Services bez IgnoreQueryFilters wycinał wizytę.
  [Fact]
  public async Task GetAppointments_returns_appointment_with_deactivated_service()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var date = TestDates.InDays(40);
    var apptId = SeedAppointment(factory.Services, seed, date, new TimeOnly(9, 0));

    DeactivateService(factory.Services, seed.ServiceId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Range query — wizyta MUSI być nadal widoczna.
    var listResponse = await ownerClient.GetAsync(
      $"/api/Appointments?startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}", ct);
    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    var list = await listResponse.Content.ReadFromJsonAsync<List<AppointmentPreviewItem>>(ct);
    Assert.NotNull(list);
    Assert.Contains(list!, a => a.Id == apptId);

    // Detail — NIE 404; nazwa usługi to sensowny fallback (pusty string przy soft-delete).
    var detailResponse = await ownerClient.GetAsync($"/api/Appointments/{apptId}", ct);
    Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    var detail = await detailResponse.Content.ReadFromJsonAsync<AppointmentDetailItem>(ct);
    Assert.NotNull(detail);
    Assert.Equal(seed.ServiceId, detail!.ServiceId);
    Assert.NotNull(detail.ServiceName); // fallback, nie NRE / NotFound
  }

  // IT-APPT-013: BUG — soft-deleted employee must NOT make existing appointments disappear.
  [Fact]
  public async Task GetAppointments_returns_appointment_with_deactivated_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var date = TestDates.InDays(41);
    // Deaktywujemy DRUGIEGO pracownika (nie Ownera — inaczej caller traci auth).
    var targetEmployeeId = SeedSecondEmployee(factory.Services, seed.TenantId, seed.ServiceId);
    var apptId = SeedAppointmentFor(factory.Services, seed.TenantId, targetEmployeeId, seed.ServiceId, seed.CustomerId, date, new TimeOnly(12, 0));

    DeactivateEmployee(factory.Services, targetEmployeeId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var listResponse = await ownerClient.GetAsync(
      $"/api/Appointments?startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}", ct);
    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    var list = await listResponse.Content.ReadFromJsonAsync<List<AppointmentPreviewItem>>(ct);
    Assert.NotNull(list);
    Assert.Contains(list!, a => a.Id == apptId);

    var detailResponse = await ownerClient.GetAsync($"/api/Appointments/{apptId}", ct);
    Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
  }

  // IT-APPT-014: „Usuń klienta” (erasure) ustawia IsActive=false, ale kafelek wizyty MUSI dalej
  // pokazywać placeholder „Klient usunięty” z Customer.Anonymize() — nie puste imię (front
  // degraduje je do „—”) i nie „Gość” (CustomerId nadal jest przypisany).
  //
  // Działa to dzięki niuansowi EF Core: globalny query-filter (TenantId && IsActive) obowiązuje
  // dla zapytania korzeniowego, ale NIE dla zbioru wciągniętego przez `join ... into ...
  // DefaultIfEmpty()`. Zachowanie jest więc poprawne przypadkiem, nie z projektu — stąd ten test.
  // Przepisanie joina na `Include`/nawigację albo na podzapytanie po `_context.Customers`
  // przywróci filtr i kafelek zgubi nazwę. Wtedy trzeba dodać `IgnoreQueryFilters()`
  // (jak przy Employees/Services obok), zachowując jawny warunek TenantId.
  [Fact]
  public async Task GetAppointments_shows_placeholder_name_for_erased_customer()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var date = TestDates.InDays(42);
    var apptId = SeedAppointment(factory.Services, seed, date, new TimeOnly(14, 0));

    AnonymizeCustomer(factory.Services, seed.CustomerId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var listResponse = await ownerClient.GetAsync(
      $"/api/Appointments?startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}", ct);
    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

    var list = await listResponse.Content.ReadFromJsonAsync<List<AppointmentCustomerItem>>(ct);
    Assert.NotNull(list);
    var tile = Assert.Single(list!, a => a.Id == apptId);

    Assert.Equal("Klient", tile.CustomerFirstName);
    Assert.Equal("usunięty", tile.CustomerLastName);
    // Wizyta ma nadal CustomerId — nie może udawać rezerwacji anonimowej („Gość”).
    Assert.False(tile.IsGuest);
    // PII faktycznie zniknęło z odpowiedzi.
    Assert.Equal(string.Empty, tile.CustomerEmail);
    Assert.Equal(string.Empty, tile.CustomerPhoneNumber);
  }

  private sealed record AppointmentPreviewItem(Guid Id, Guid EmployeeId, string ServiceName);
  private sealed record AppointmentDetailItem(Guid Id, Guid ServiceId, string ServiceName);
  private sealed record AppointmentCustomerItem(
    Guid Id,
    string CustomerFirstName,
    string CustomerLastName,
    string CustomerEmail,
    string CustomerPhoneNumber,
    bool IsGuest);

  private static Guid SeedAppointmentFor(
    IServiceProvider rootServices,
    Guid tenantId,
    Guid employeeId,
    Guid serviceId,
    Guid? customerId,
    DateOnly date,
    TimeOnly startTime)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = new Appointment(
      tenantId, employeeId, serviceId, customerId,
      date, startTime, startTime.AddMinutes(30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }

  private static Guid SeedSecondEmployee(IServiceProvider rootServices, Guid tenantId, Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<App.Infrastructure.Persistence.ApplicationDbContext>();
    var second = new App.Domain.Aggregates.EmployeeAggregate.Employee(
      tenantId, null, "Second", "Worker", "second@worker.local");
    var dayRanges = (IReadOnlyCollection<App.Domain.Common.TimeRange>)new List<App.Domain.Common.TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    second.SetWeeklySchedule(Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges));
    second.AssignService(tenantId, serviceId, 30, new Money(80m, "PLN"));
    db.Employees.Add(second);
    db.SaveChanges();
    return second.Id;
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices,
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = new Appointment(
      seed.TenantId, seed.EmployeeId, seed.ServiceId, seed.CustomerId,
      date, startTime, startTime.AddMinutes(30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }

  private static decimal GetAppointmentPrice(IServiceProvider rootServices, Guid appointmentId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    return db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(a => a.Id == appointmentId)
      .Select(a => a.TotalPrice.Amount)
      .First();
  }

  private static void DeactivateEmployee(IServiceProvider rootServices, Guid employeeId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var emp = db.Employees.IgnoreQueryFilters().First(e => e.Id == employeeId);
    emp.Deactivate();
    db.SaveChanges();
  }

  private static void DeactivateService(IServiceProvider rootServices, Guid serviceId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var svc = db.Services.IgnoreQueryFilters().First(s => s.Id == serviceId);
    svc.Deactivate();
    db.SaveChanges();
  }

  private static void DeactivateCustomer(IServiceProvider rootServices, Guid customerId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var cust = db.Customers.IgnoreQueryFilters().First(c => c.Id == customerId);
    cust.Deactivate();
    db.SaveChanges();
  }

  /// <summary>Odpowiednik „Usuń klienta” — erasure PII + IsActive=false (patrz CustomerErasure).</summary>
  private static void AnonymizeCustomer(IServiceProvider rootServices, Guid customerId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var cust = db.Customers.IgnoreQueryFilters().First(c => c.Id == customerId);
    cust.Anonymize();
    db.SaveChanges();
  }
}
