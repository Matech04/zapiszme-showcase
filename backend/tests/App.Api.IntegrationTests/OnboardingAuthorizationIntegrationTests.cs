using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Bramki roli na kreatorze onboardingu — preflight 2026-07-31, dwa znaleziska HIGH.
///
/// `OnboardingController` miał klasowe `[Authorize]` bez polityki, więc cztery mutacje salonu
/// (nazwa, PUBLICZNY slug rezerwacji, branża z usługami, grafik, tryb potwierdzania) stały otworem
/// dla każdej zalogowanej roli. Konto „Recepcja" przechodziło nawet bramkę potwierdzonego telefonu,
/// bo jest zakładane z `PhoneNumberConfirmed = true` — mimo obietnicy przy definicji polityk
/// w Program.cs, że Kiosk nie zmienia ustawień.
/// </summary>
public class OnboardingAuthorizationIntegrationTests
{
  private static readonly object ProfileBody = new
  {
    firstName = "Kto",
    lastName = "Kolwiek",
    salonName = "Przejety Salon",
    salonSlug = "przejety-salon",
  };

  public static TheoryData<string, object> Mutations() => new()
  {
    { "/api/onboarding/profile", ProfileBody },
    { "/api/onboarding/industry", new { industryKey = "brwi", services = Array.Empty<object>() } },
    { "/api/onboarding/schedule", new { slotMode = 0, useAdHoc = true } },
    { "/api/onboarding/complete", new { confirmationMode = 0 } },
  };

  [Theory]
  [MemberData(nameof(Mutations))]
  public async Task Employee_cannot_run_onboarding_mutations(string path, object body)
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(path, body, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  /// <summary>
  /// Kiosk osobno od Employee: to on jest tu najciekawszy, bo jako jedyny przechodzi gate
  /// `EmailConfirmed &amp;&amp; PhoneNumberConfirmed` w `CompleteProfile` i bez polityki mógł
  /// przemianować salon oraz podmienić jego publiczny adres rezerwacji.
  /// </summary>
  [Theory]
  [MemberData(nameof(Mutations))]
  public async Task Kiosk_cannot_run_onboarding_mutations(string path, object body)
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(path, body, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  /// <summary>Kontrola pozytywna: właściciel nadal przechodzi — polityka nie zamyka kreatora.</summary>
  [Fact]
  public async Task Owner_can_still_read_onboarding_state()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  /// <summary>
  /// Odczyt stanu MUSI zostać otwarty dla pracownika: woła go bootstrap każdej roli, żeby ustalić,
  /// czy user jest w kreatorze. Zamknięcie go polityką odbiłoby pracownika w pętli na `/setup`.
  /// </summary>
  [Fact]
  public async Task Employee_can_still_read_onboarding_state()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  /// <summary>
  /// Drugie znalezisko HIGH: gałąź `UseAdHoc` w `SetOnboardingScheduleCommand` dezaktywowała
  /// WSZYSTKIE aktywne grafiki i robiła `return` PRZED delegacją, która jako jedyna autoryzowała.
  /// `ResolveScheduleDay` filtruje po `IsActive`, więc publiczny booking przestawał generować
  /// terminy — cichy DoS na sprzedaż salonu, wykonalny przez zwykłego pracownika.
  ///
  /// Sam status 403 to za mało: trzeba udowodnić, że NIC się nie zapisało. Poprzednia wersja
  /// commitowała zmiany przed odrzuceniem żądania, więc test na samym kodzie odpowiedzi
  /// przeszedłby również dla wadliwego kodu.
  /// </summary>
  [Fact]
  public async Task Employee_cannot_wipe_owner_schedule_via_adhoc_branch()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var activeBefore = await CountActiveSchedulesAsync(factory, ct);

    var response = await client.PostAsJsonAsync(
      "/api/onboarding/schedule",
      new { slotMode = 0, useAdHoc = true },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

    var activeAfter = await CountActiveSchedulesAsync(factory, ct);
    Assert.Equal(activeBefore, activeAfter);

    using var verify = factory.Services.CreateScope();
    var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var adHocFlags = await db.Employees
      .IgnoreQueryFilters()
      .Where(e => e.TenantId == seed.TenantId)
      .Select(e => e.UsesAdHocSchedule)
      .ToListAsync(ct);

    Assert.DoesNotContain(true, adHocFlags);
  }

  private static async Task<int> CountActiveSchedulesAsync(
    BookingApiApplicationFactory factory, CancellationToken ct)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    return await db.Employees
      .IgnoreQueryFilters()
      .SelectMany(e => e.Schedules)
      .CountAsync(s => s.IsActive, ct);
  }
}
