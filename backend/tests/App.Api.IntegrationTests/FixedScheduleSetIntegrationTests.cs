using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Regresja: zapis grafiku w trybie stałych slotów przez panel (POST /api/Employees/{id}/employee-schedules).
/// Wcześniej walidator (EmployeeScheduleDayDtoValidator) bezwarunkowo wymagał WorkRanges,
/// więc każdy zapis grafiku stałego (dzień ma tylko FixedStartTimes, WorkRanges puste) padał na 400 —
/// nie dało się stworzyć ani edytować grafiku stałego z dashboardu.
/// </summary>
public sealed class FixedScheduleSetIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

  private sealed record ScheduleIdDto(Guid id);

  private static object BuildFixedSchedule(Guid? id) => new
  {
    id,
    activeFrom = TestDates.IsoInDays(-60),
    activeTo = "9999-12-31",
    numberOfCycles = 1,
    days = new object[]
    {
      new
      {
        cycleIndex = 1,
        workRanges = Array.Empty<object>(),
        breaks = Array.Empty<object>(),
        fixedStartTimes = new[] { "09:00:00", "12:00:00", "15:00:00" },
      },
    },
    slotGenerationMode = 1,
  };

  [Fact]
  public async Task Owner_can_create_fixed_schedule_on_new_employee()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Fresh employee has no schedule yet → exercises the pure CREATE path (AddSchedule).
    var createResp = await ownerClient.PostAsJsonAsync(
      "/api/Employees",
      new { firstName = "Fixed", lastName = "Worker", email = "fixed@worker.local" },
      ct);
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
    var employeeId = await createResp.Content.ReadFromJsonAsync<Guid>(JsonOpts, ct);

    var response = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{employeeId}/employee-schedules",
      BuildFixedSchedule(id: null),
      ct);

    var body = await response.Content.ReadAsStringAsync(ct);
    Assert.True(response.IsSuccessStatusCode, $"Create fixed schedule failed: {(int)response.StatusCode}: {body}");
  }

  [Fact]
  public async Task Owner_can_edit_existing_schedule_to_fixed()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var listResp = await ownerClient.GetAsync($"/api/Employees/{seed.EmployeeId}/employee-schedules", ct);
    Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    var schedules = await listResp.Content.ReadFromJsonAsync<List<ScheduleIdDto>>(JsonOpts, ct);
    var scheduleId = schedules!.First().id;

    var response = await ownerClient.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/employee-schedules",
      BuildFixedSchedule(scheduleId),
      ct);

    var body = await response.Content.ReadAsStringAsync(ct);
    Assert.True(response.IsSuccessStatusCode, $"Edit to fixed schedule failed: {(int)response.StatusCode}: {body}");
  }
}
