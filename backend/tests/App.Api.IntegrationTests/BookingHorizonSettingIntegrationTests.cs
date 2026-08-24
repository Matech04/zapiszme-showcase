using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Api.IntegrationTests;

/// <summary>
/// Horyzont rezerwacji jako USTAWIENIE salonu, nie stała.
///
/// Pole <c>Tenant.BookingHorizonDays</c> istniało i było egzekwowane, ale
/// <c>UpdateCurrentSalonSettings</c> nigdy go nie przekazywał do agregatu — funkcja jechała na
/// sztywnych 120 dniach i salon nie miał jak jej zmienić inaczej niż UPDATE-em w bazie.
/// Te testy pilnują pełnej pętli: zapis w panelu → odczyt w publicznej konfiguracji → realny wpływ
/// na wystawiane terminy.
/// </summary>
public sealed class BookingHorizonSettingIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record SlotItem(string Slot, bool IsPreferred);
  private sealed record PublicSalonInfo(string Name, int BookingHorizonDays);
  private sealed record SalonSettings(int BookingHorizonDays);

  /// <summary>
  /// Limity rate-limitingu podniesione: te testy robią kilka publicznych odczytów pod rząd, a
  /// limiter jest współdzielony między równolegle biegnącymi testami (jedno loopback-IP).
  /// Wzorzec z BookingAbuseHardCapsIntegrationTests.
  /// </summary>
  private static WebApplicationFactory<Program> WithRelaxedLimits(BookingApiApplicationFactory baseFactory) =>
    baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseSetting("RateLimiting:PublicBookingRead:PermitLimit", "1000");
      builder.UseSetting("RateLimiting:Global:PermitLimit", "1000");
    });

  /// <summary>Klient właściciela z fabryki po WithWebHostBuilder (rozszerzenie CreateOwnerClient
  /// jest przypięte do BookingApiApplicationFactory, a tu mamy typ bazowy).</summary>
  private static HttpClient OwnerClient(WebApplicationFactory<Program> factory)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId, IntegrationTestUserIds.SalonOwner.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    return client;
  }

  private static Task<HttpResponseMessage> PutHorizonAsync(
    HttpClient ownerClient, string slug, int? horizonDays, CancellationToken ct) =>
    ownerClient.PutAsJsonAsync(
      "/api/SalonSettings",
      new
      {
        name = "REST API Seed Salon",
        slug,
        customerVerificationChannel = 0,
        appointmentSlotStepMinutes = 15,
        timeZoneId = "Europe/Warsaw",
        currency = "PLN",
        bookingHorizonDays = horizonDays,
      },
      ct);

  [Fact]
  public async Task Owner_can_shorten_the_booking_horizon_and_it_takes_effect_publicly()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRelaxedLimits(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = OwnerClient(factory);
    var anonymous = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Przed zmianą: dzień +45 mieści się w domyślnych 120 dniach.
    var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);
    var before = await anonymous.GetAsync(
      $"/api/booking/{seed.TenantSlug}/appointments/available-slots" +
      $"?date={target:yyyy-MM-dd}&employeeId={seed.EmployeeId}&serviceIds={seed.ServiceId}", ct);
    var slotsBefore = await before.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.NotEmpty(slotsBefore!);

    var put = await PutHorizonAsync(owner, seed.TenantSlug, 30, ct);
    Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

    // Publiczna konfiguracja niesie nową wartość — fronty przestały hardkodować stałą.
    var salon = await anonymous.GetAsync($"/api/booking/{seed.TenantSlug}/public-salon", ct);
    var info = await salon.Content.ReadFromJsonAsync<PublicSalonInfo>(JsonRead, ct);
    Assert.Equal(30, info!.BookingHorizonDays);

    // Po zmianie ten sam dzień jest już poza horyzontem.
    var after = await anonymous.GetAsync(
      $"/api/booking/{seed.TenantSlug}/appointments/available-slots" +
      $"?date={target:yyyy-MM-dd}&employeeId={seed.EmployeeId}&serviceIds={seed.ServiceId}", ct);
    var slotsAfter = await after.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct);
    Assert.Empty(slotsAfter!);

    // Kontrola negatywna: dzień wewnątrz nowego, krótszego okna nadal działa.
    var near = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
    var nearSlots = await anonymous.GetAsync(
      $"/api/booking/{seed.TenantSlug}/appointments/available-slots" +
      $"?date={near:yyyy-MM-dd}&employeeId={seed.EmployeeId}&serviceIds={seed.ServiceId}", ct);
    Assert.NotEmpty((await nearSlots.Content.ReadFromJsonAsync<List<SlotItem>>(JsonRead, ct))!);
  }

  [Fact]
  public async Task Horizon_is_returned_in_salon_settings_for_the_panel()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRelaxedLimits(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = OwnerClient(factory);
    var ct = TestContext.Current.CancellationToken;

    await PutHorizonAsync(owner, seed.TenantSlug, 200, ct);

    var response = await owner.GetAsync("/api/SalonSettings", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var settings = await response.Content.ReadFromJsonAsync<SalonSettings>(JsonRead, ct);

    Assert.Equal(200, settings!.BookingHorizonDays);
  }

  [Fact]
  public async Task Omitting_the_horizon_keeps_the_current_value()
  {
    // Null = „nie ruszaj". Bez tego każdy zapis innych ustawień cicho resetowałby horyzont.
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRelaxedLimits(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = OwnerClient(factory);
    var ct = TestContext.Current.CancellationToken;

    await PutHorizonAsync(owner, seed.TenantSlug, 45, ct);
    await PutHorizonAsync(owner, seed.TenantSlug, null, ct);

    var settings = await (await owner.GetAsync("/api/SalonSettings", ct))
      .Content.ReadFromJsonAsync<SalonSettings>(JsonRead, ct);

    Assert.Equal(45, settings!.BookingHorizonDays);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-5)]
  [InlineData(2000)]
  public async Task Horizon_outside_the_allowed_range_is_rejected(int invalid)
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRelaxedLimits(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var owner = OwnerClient(factory);
    var ct = TestContext.Current.CancellationToken;

    var response = await PutHorizonAsync(owner, seed.TenantSlug, invalid, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
