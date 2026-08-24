using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;

namespace App.Api.IntegrationTests;

/// <summary>
/// Cache slug→tenant w <c>TenantIdentifierMiddleware</c> (TTL 5 min) zakładał, że slug jest
/// niezmienny. Nie jest: właściciel zmienia go w ustawieniach salonu, a unikalny indeks natychmiast
/// zwalnia stary slug do przejęcia przez inny salon.
///
/// Bez unieważnienia powstawało okno, w którym publiczne żądania na przejęty slug rozwiązywały się
/// na POPRZEDNIEGO tenanta — czyli rezerwacje klientów jednego salonu trafiały do kalendarza
/// drugiego. Write-guard tego nie wykrywa, bo <c>TenantId</c> jest wtedy spójny na całej ścieżce.
///
/// UWAGA co do doboru endpointu: testujemy przez <c>/employees</c>, bo ten handler jest
/// <c>TenantHandler</c> i filtruje po <c>TenantId</c> ROZWIĄZANYM przez middleware — czyli po
/// wartości z cache'u. <c>/public-salon</c> do tego NIE służy: czyta tenanta po slugu wprost
/// (<c>t.Slug == request.Slug</c>), więc omija cache i przechodzi niezależnie od tej naprawy.
/// </summary>
public sealed class TenantSlugCacheInvalidationIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record BookingEmployeeItem(Guid Id, string FirstName, string LastName);

  private static HttpClient OwnerClientFor(BookingApiApplicationFactory factory, Guid ownerUserId)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId, ownerUserId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    return client;
  }

  /// <summary>Minimalny payload PUT /api/SalonSettings — zmienia slug, resztę zostawia sensowną.</summary>
  private static Task<HttpResponseMessage> ChangeSlugAsync(
    HttpClient ownerClient, string name, string newSlug, CancellationToken ct) =>
    ownerClient.PutAsJsonAsync(
      "/api/SalonSettings",
      new
      {
        name,
        slug = newSlug,
        customerVerificationChannel = 0,
        appointmentSlotStepMinutes = 15,
        timeZoneId = "Europe/Warsaw",
        currency = "PLN",
      },
      ct);

  private static async Task<List<BookingEmployeeItem>?> GetEmployeesAsync(
    HttpClient anonymous, string slug, CancellationToken ct)
  {
    var response = await anonymous.GetAsync($"/api/booking/{slug}/employees", ct);
    return response.StatusCode == HttpStatusCode.OK
      ? await response.Content.ReadFromJsonAsync<List<BookingEmployeeItem>>(JsonRead, ct)
      : null;
  }

  [Fact]
  public async Task Slug_taken_over_by_another_salon_serves_the_new_owners_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var first = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var anonymous = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var contestedSlug = first.TenantSlug;

    // 1. Rozgrzewamy cache: slug → tenant PIERWSZY.
    var warmup = await GetEmployeesAsync(anonymous, contestedSlug, ct);
    Assert.NotNull(warmup);
    Assert.Contains(warmup, e => e.Id == first.EmployeeId);

    // 2. Pierwszy salon zmienia slug — sporny slug zostaje zwolniony.
    var movedAway = await ChangeSlugAsync(
      OwnerClientFor(factory, IntegrationTestUserIds.SalonOwner),
      "REST API Seed Salon", contestedSlug + "-moved", ct);
    Assert.Equal(HttpStatusCode.NoContent, movedAway.StatusCode);

    // 3. Drugi salon przejmuje zwolniony slug.
    var takenOver = await ChangeSlugAsync(
      OwnerClientFor(factory, IntegrationTestUserIds.SecondSalonOwner),
      "REST API Seed Salon 2", contestedSlug, ct);
    Assert.Equal(HttpStatusCode.NoContent, takenOver.StatusCode);

    // 4. Publiczne żądanie na sporny slug MUSI zwrócić personel DRUGIEGO salonu.
    //    Przed naprawą przez do 5 minut wracał tu pierwszy — a tam poszłyby cudze rezerwacje.
    var afterTakeover = await GetEmployeesAsync(anonymous, contestedSlug, ct);
    Assert.NotNull(afterTakeover);
    Assert.Contains(afterTakeover, e => e.Id == second.EmployeeId);
    Assert.DoesNotContain(afterTakeover, e => e.Id == first.EmployeeId);
  }

  [Fact]
  public async Task Renamed_salon_is_reachable_under_the_new_slug_and_gone_from_the_old_one()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var anonymous = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var oldSlug = seed.TenantSlug;
    var newSlug = oldSlug + "-renamed";

    var warmup = await GetEmployeesAsync(anonymous, oldSlug, ct);
    Assert.NotNull(warmup);
    Assert.Contains(warmup, e => e.Id == seed.EmployeeId);

    var renamed = await ChangeSlugAsync(
      OwnerClientFor(factory, IntegrationTestUserIds.SalonOwner),
      "REST API Seed Salon", newSlug, ct);
    Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

    // Stary slug nie należy już do nikogo — nie wolno serwować danych z nieważnego wpisu cache'u.
    var stale = await GetEmployeesAsync(anonymous, oldSlug, ct);
    Assert.True(stale is null || stale.Count == 0, "Stary slug nie może dalej zwracać personelu salonu.");

    // Nowy slug działa od razu.
    var fresh = await GetEmployeesAsync(anonymous, newSlug, ct);
    Assert.NotNull(fresh);
    Assert.Contains(fresh, e => e.Id == seed.EmployeeId);
  }
}
