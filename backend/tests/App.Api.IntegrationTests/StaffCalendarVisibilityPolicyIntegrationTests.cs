using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// APPT-013 — `StaffCalendarVisibilityPolicy` rozszerza dostęp pracownika ponad „tylko swój
/// kalendarz". Owner/Manager są niezmienni; tu sprawdzamy tylko rolę Employee × 3 polityki.
/// </summary>
public sealed class StaffCalendarVisibilityPolicyIntegrationTests
{
  [Fact]
  public async Task Default_policy_is_TeamFull_employee_can_read_other()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(40), new TimeOnly(10, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Appointments/{otherApptId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task OwnCalendarOnly_policy_employee_cannot_read_other()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    UpdatePolicy(factory.Services, own.TenantId, StaffCalendarVisibilityPolicy.OwnCalendarOnly);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(40), new TimeOnly(10, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Appointments/{otherApptId}", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task TeamReadOnly_policy_lets_employee_read_other_employees_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    UpdatePolicy(factory.Services, own.TenantId, StaffCalendarVisibilityPolicy.TeamReadOnly);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(41), new TimeOnly(11, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync($"/api/Appointments/{otherApptId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task TeamReadOnly_policy_lets_employee_list_other_employees_range()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    UpdatePolicy(factory.Services, own.TenantId, StaffCalendarVisibilityPolicy.TeamReadOnly);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(42), new TimeOnly(12, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.GetAsync(
      $"/api/Appointments?startDate={TestDates.IsoInDays(30)}&endDate={TestDates.IsoInDays(60)}&employeeId={secondEmployeeId}", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task TeamReadOnly_policy_still_blocks_mutation_of_other_employees_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    UpdatePolicy(factory.Services, own.TenantId, StaffCalendarVisibilityPolicy.TeamReadOnly);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(43), new TimeOnly(13, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    // 5 = Canceled
    var response = await employeeClient.PatchAsync(
      $"/api/Appointments/{otherApptId}/status", JsonContent.Create(5), ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task TeamFull_policy_lets_employee_mutate_other_employees_appointment()
  {
    using var factory = new BookingApiApplicationFactory();
    var own = RestApiIntegrationSeed.Seed(factory.Services);
    UpdatePolicy(factory.Services, own.TenantId, StaffCalendarVisibilityPolicy.TeamFull);

    var secondEmployeeId = SeedSecondEmployee(factory.Services, own.TenantId);
    var otherApptId = SeedAppointment(
      factory.Services, own.TenantId, secondEmployeeId, own.ServiceId,
      TestDates.InDays(44), new TimeOnly(14, 0));

    var employeeClient = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await employeeClient.PatchAsync(
      $"/api/Appointments/{otherApptId}/status", JsonContent.Create(5), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private static void UpdatePolicy(IServiceProvider rootServices, Guid tenantId, StaffCalendarVisibilityPolicy policy)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.Single(t => t.Id == tenantId);
    tenant.Update(tenant.Name, tenant.Slug, staffCalendarVisibilityPolicy: policy);
    db.SaveChanges();
  }

  private static Guid SeedSecondEmployee(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = new App.Domain.Aggregates.EmployeeAggregate.Employee(
      tenantId, userId: null, "Other", "Mate", $"other-{Guid.NewGuid():N}@policy-test.local");
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
  }

  private static Guid SeedAppointment(
    IServiceProvider rootServices, Guid tenantId, Guid employeeId, Guid serviceId,
    DateOnly date, TimeOnly startTime)
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
