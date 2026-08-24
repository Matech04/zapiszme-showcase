using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// APPT-014 — CreateAppointment i Reschedule rzucają EmployeeServiceMissingException,
/// gdy pracownik nie ma przypisanej żądanej usługi.
/// </summary>
public sealed class AppointmentEmployeeServiceMismatchIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  // API-APPT-001: APPT-014 — POST /api/Appointments z nieprzypisaną usługą zwraca 400
  [Fact]
  public async Task Post_appointment_where_employee_lacks_service_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var unassignedServiceId = SeedServiceNotAssignedToEmployee(factory.Services, seed.TenantId, seed.VatRateId, seed.ServiceCategoryId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { unassignedServiceId },
        date = TestDates.IsoInDays(40),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsWithErrorCode>(JsonOpts, ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.EmployeeServiceMissing, problem.ErrorCode);
  }

  // APP-APPT-026: APPT-014 — CreateAppointmentCommand throws EmployeeServiceMissing
  // Testowane przez ten sam endpoint — pracownik bez usługi → 400 z odpowiednim errorCode.
  // Weryfikacja pełnej ścieżki: HTTP → Handler → Employee.CalculateTotalPrice.
  [Fact]
  public async Task Post_appointment_where_employee_has_no_services_at_all_returns_bad_request()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var employeeWithNoServices = SeedEmployeeWithNoServices(factory.Services, seed.TenantId);

    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = employeeWithNoServices,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(41),
        startTime = "10:00:00",
        customerId = (Guid?)null,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsWithErrorCode>(JsonOpts, ct);
    Assert.NotNull(problem);
    Assert.Equal(ErrorCodes.EmployeeServiceMissing, problem.ErrorCode);
  }

  private static Guid SeedServiceNotAssignedToEmployee(
    IServiceProvider rootServices,
    Guid tenantId,
    Guid vatRateId,
    Guid categoryId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var svc = new Service(tenantId, categoryId, vatRateId, "Nieprzypisana usługa", new Money(60m, "PLN"), 45);
    db.Services.Add(svc);
    db.SaveChanges();
    return svc.Id;
  }

  private static Guid SeedEmployeeWithNoServices(IServiceProvider rootServices, Guid tenantId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var emp = new Employee(tenantId, null, "Bez", "Usług", "noservices@test.local");
    db.Employees.Add(emp);
    db.SaveChanges();
    return emp.Id;
  }

  private sealed record ProblemDetailsWithErrorCode(string? ErrorCode);
}
