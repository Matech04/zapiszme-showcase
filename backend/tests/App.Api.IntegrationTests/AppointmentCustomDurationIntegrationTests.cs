using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Niestandardowy czas wizyty (per-wizyta override personelu): tworzenie z override (POST),
/// zmiana przez dedykowany endpoint (PATCH {id}/duration) i powrót do standardu (null).
/// Usługa w seedzie ma standardowy czas 30 min.
/// </summary>
public sealed class AppointmentCustomDurationIntegrationTests
{
  [Fact]
  public async Task Create_with_custom_duration_shortens_block()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = TestDates.IsoInDays(20),
      startTime = "10:00:00",
      createAsBooked = true,
      customDurationMinutes = 45, // standard = 30
    }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var apptId = await response.Content.ReadFromJsonAsync<Guid>(ct);

    var reloaded = await Reload(factory, apptId, ct);
    Assert.Equal(45, reloaded.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(10, 45), reloaded.EndTime);
  }

  [Fact]
  public async Task Patch_duration_changes_then_reset_to_standard()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Utwórz wizytę ze standardowym czasem (30 min).
    var createResp = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = TestDates.IsoInDays(21),
      startTime = "11:00:00",
      createAsBooked = true,
    }, ct);
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
    var apptId = await createResp.Content.ReadFromJsonAsync<Guid>(ct);

    // Skróć do 20 min.
    var patchResp = await ownerClient.PatchAsync(
      $"/api/Appointments/{apptId}/duration",
      JsonContent.Create(new { durationMinutes = 20 }),
      ct);
    Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

    var shortened = await Reload(factory, apptId, ct);
    Assert.Equal(20, shortened.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(11, 20), shortened.EndTime);

    // Powrót do standardu (null) → 30 min, override wyczyszczony.
    var resetResp = await ownerClient.PatchAsync(
      $"/api/Appointments/{apptId}/duration",
      JsonContent.Create(new { durationMinutes = (int?)null }),
      ct);
    Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

    var reset = await Reload(factory, apptId, ct);
    Assert.Null(reset.CustomDurationMinutes);
    Assert.Equal(new TimeOnly(11, 30), reset.EndTime);
  }

  [Fact]
  public async Task Shortening_appointment_frees_the_slot_for_an_adjacent_booking()
  {
    // A: 10:00 z niestandardowym 90 min → blokuje 10:00–11:30.
    // B: 10:45 (standard 30 → 11:15) — koliduje z A → odrzucone (slot niedostępny).
    // Skracamy A do 30 min → 10:00–10:30. Teraz B 10:45 się mieści → przyjęte.
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;
    var day = TestDates.IsoInDays(24);

    var createA = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = day,
      startTime = "10:00:00",
      createAsBooked = true,
      customDurationMinutes = 90,
    }, ct);
    Assert.Equal(HttpStatusCode.OK, createA.StatusCode);
    var aId = await createA.Content.ReadFromJsonAsync<Guid>(ct);

    // B koliduje z długim blokiem A.
    var bCollide = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = day,
      startTime = "10:45:00",
      createAsBooked = true,
    }, ct);
    Assert.Equal(HttpStatusCode.BadRequest, bCollide.StatusCode); // appointment.slot_unavailable

    // Skróć A → zwalnia 10:30–11:30.
    var shorten = await ownerClient.PatchAsync(
      $"/api/Appointments/{aId}/duration",
      JsonContent.Create(new { durationMinutes = 30 }),
      ct);
    Assert.Equal(HttpStatusCode.OK, shorten.StatusCode);

    // B teraz się mieści.
    var bOk = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = day,
      startTime = "10:45:00",
      createAsBooked = true,
    }, ct);
    Assert.Equal(HttpStatusCode.OK, bOk.StatusCode);
  }

  [Fact]
  public async Task Get_appointment_by_id_exposes_custom_duration()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var create = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = TestDates.IsoInDays(26),
      startTime = "12:00:00",
      createAsBooked = true,
      customDurationMinutes = 50,
    }, ct);
    Assert.Equal(HttpStatusCode.OK, create.StatusCode);
    var id = await create.Content.ReadFromJsonAsync<Guid>(ct);

    var detail = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/Appointments/{id}", ct);
    Assert.Equal(50, detail.GetProperty("customDurationMinutes").GetInt32());
  }

  [Fact]
  public async Task Custom_duration_wrapping_past_midnight_is_rejected()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // 23:30 + 60 min zawija za północ → nieprawidłowy przedział czasu (odrzucone, nie 200).
    var response = await ownerClient.PostAsJsonAsync("/api/Appointments", new
    {
      employeeId = seed.EmployeeId,
      serviceIds = new[] { seed.ServiceId },
      date = TestDates.IsoInDays(28),
      startTime = "23:30:00",
      createAsBooked = true,
      customDurationMinutes = 60,
    }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static async Task<App.Domain.Aggregates.AppointmentAggregate.Appointment> Reload(
    BookingApiApplicationFactory factory, Guid apptId, CancellationToken ct)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    return await db.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == apptId, ct);
  }
}
