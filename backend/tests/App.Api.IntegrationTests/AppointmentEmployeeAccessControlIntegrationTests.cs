using System.Net;
using System.Net.Http.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// APPT-011 — pracownik widzi tylko własne wizyty; GET /{id} cudzej wizyty zwraca 403.
/// Dotyczy salonu z polityką `OwnCalendarOnly` — nowe tenanty mają domyślnie `TeamFull`,
/// więc testy ustawiają ją jawnie.
/// </summary>
public sealed class AppointmentEmployeeAccessControlIntegrationTests
{
  // API-APPT: APPT-011 Security — "Employee can only read their own appointments, not other employees'"
  [Fact]
  public async Task Employee_list_query_with_other_employee_id_returns_forbid()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    SetOwnCalendarOnly(factory.Services, own.TenantId);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    SeedAppointment(factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(10), new TimeOnly(11, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    // Employee queries list filtered by the OTHER employee's id — should be Forbid
    var response = await employeeClient.GetAsync(
      $"/api/Appointments?startDate={TestDates.IsoInDays(0)}&endDate={TestDates.IsoInDays(30)}&employeeId={secondEmployeeId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // API-APPT: APPT-011 Security — "Employee receives Forbid on GET /{id} for another employee's appointment"
  [Fact]
  public async Task Employee_get_by_id_returns_forbid_for_other_employees_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    SetOwnCalendarOnly(factory.Services, own.TenantId);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(11), new TimeOnly(10, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Appointments/{otherApptId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // API-APPT: APPT-011 — employee correctly reads their OWN appointment
  [Fact]
  public async Task Employee_get_by_id_returns_ok_for_own_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var ownApptId = SeedAppointment(factory.Services, own.TenantId, own.EmployeeId, own.ServiceId,
      TestDates.InDays(12), new TimeOnly(14, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Appointments/{ownApptId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  /// <summary>
  /// APPT-011 Security — reschedule sprawdzał tylko pracownika DOCELOWEGO, więc pracownik podający
  /// własne EmployeeId przenosił cudzą wizytę na swój kalendarz. Preflight 2026-07-09, MEDIUM.
  /// </summary>
  [Fact]
  public async Task Employee_reschedule_of_other_employees_appointment_returns_forbid()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    SetOwnCalendarOnly(factory.Services, own.TenantId);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(13), new TimeOnly(9, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    // Cel przypisania = własne id (przechodzi starą kontrolę), wizyta = cudza.
    var response = await employeeClient.PatchAsJsonAsync(
      $"/api/Appointments/{otherApptId}/reschedule",
      new
      {
        employeeId = own.EmployeeId,
        serviceIds = new[] { own.ServiceId },
        date = TestDates.IsoInDays(14),
        startTime = "12:00:00",
        ignoreSchedule = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  private static void SetOwnCalendarOnly(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.Single(t => t.Id == tenantId);
    tenant.Update(tenant.Name, tenant.Slug,
      staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.OwnCalendarOnly);
    db.SaveChanges();
  }

  private static Guid SeedSecondEmployee(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var employee = new App.Domain.Aggregates.EmployeeAggregate.Employee(
      tenantId, userId: null, "Other", "Employee", "other@acl-test.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices,
    Guid tenantId,
    Guid employeeId,
    Guid serviceId,
    DateOnly date,
    TimeOnly startTime)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = new Appointment(
      tenantId, employeeId, serviceId, null,
      date, startTime, startTime.AddMinutes(30),
      AppointmentStatus.Booked,
      new Money(80m, "PLN"),
      string.Empty,
      null);
    db.Appointments.Add(appt);
    db.SaveChanges();
    return appt.Id;
  }
}
