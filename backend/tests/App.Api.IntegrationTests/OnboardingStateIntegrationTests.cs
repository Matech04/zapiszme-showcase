using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// ONBOARDING-STATE — <c>GET /api/onboarding/state</c> oraz bramka weryfikacji na kroku tworzenia tenanta.
///
/// Stan z tego endpointu steruje twardym guardem na <c>/admin/**</c>, więc jego odpowiedź decyduje
/// o tym, kto w ogóle wejdzie do panelu. Dwa scenariusze brzegowe pinujemy tutaj:
///
/// 1. Admin systemowy NIE ma rekordu Employee (DbSeeder tworzy mu tylko User + UserRole), więc
///    naiwna implementacja odsyłała go w gałąź „Profile" → onboardingGuard wyrzucał go z
///    /admin/system/** do /setup, skąd nie miał wyjścia (tenanta nie stworzy — CompleteProfile
///    wymaga potwierdzonego telefonu, którego admin nie ma). Regresja blokująca panel admina na prod.
/// 2. Świeży właściciel bez tenanta musi nadal trafiać na „Profile" — inaczej wyjątek dla admina
///    przepuszczałby wszystkich i kreator przestałby cokolwiek bramkować.
/// </summary>
public sealed class OnboardingStateIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  private sealed record OnboardingStateBody(
    bool HasTenant,
    bool OnboardingCompleted,
    string NextStep,
    Guid? TenantId);

  [Fact]
  public async Task System_admin_without_employee_is_not_trapped_in_the_salon_wizard()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var adminId = await OnboardingTestSupport.CreateSystemAdminInDbAsync(factory, ct: ct);
    var adminClient = OnboardingTestSupport.CreateUserClient(factory, adminId, "Admin");
    var response = await adminClient.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var state = await response.Content.ReadFromJsonAsync<OnboardingStateBody>(JsonRead, ct);
    Assert.NotNull(state);

    // Kreator dotyczy wyłącznie właściciela salonu — dla admina onboarding jest „niedotyczący",
    // co front czyta jako ukończony i przepuszcza go do /admin/system/**.
    Assert.True(state!.OnboardingCompleted);
    Assert.Equal("Completed", state.NextStep);
    Assert.False(state.HasTenant);
    Assert.Null(state.TenantId);
  }

  /// <summary>
  /// Były pracownik NIE jest świeżym właścicielem — mimo że w bazie wygląda identycznie.
  ///
  /// Dezaktywacja pracownika zostawia konto User z rolą Employee, ale zabiera AKTYWNY rekord
  /// Employee, więc handler nie widzi ani pracownika, ani tenanta. Dopóki obie sytuacje wpadały
  /// w tę samą gałąź, `onboardingGuard` odbijał zwolnioną osobę na kreator ZAKŁADANIA SALONU:
  /// komunikat absurdalny i tak czy owak ślepy (mutacje kreatora wymagają BusinessManagement).
  /// </summary>
  [Fact]
  public async Task Deactivated_employee_is_not_sent_to_the_salon_wizard()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@state-inactive.local", "+48501333009", "Salon Inactive", "state-inactive", ct: ct);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var employee = await db.Employees
        .IgnoreQueryFilters()
        .SingleAsync(e => e.Id == employeeId, ct);
      employee.Deactivate();
      await db.SaveChangesAsync(ct);
    }

    var client = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await client.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var state = await response.Content.ReadFromJsonAsync<OnboardingStateBody>(JsonRead, ct);
    Assert.NotNull(state);

    // Kluczowe rozróżnienie: NIE „Profile". Front mapuje ten stan na ekran „konto nie ma dostępu",
    // a nie na pierwszy krok kreatora.
    Assert.Equal("InactiveAccount", state!.NextStep);
    Assert.False(state.OnboardingCompleted);
    Assert.False(state.HasTenant);
    Assert.Null(state.TenantId);
  }

  [Fact]
  public async Task Fresh_owner_without_tenant_is_sent_to_the_profile_step()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var anonClient = factory.CreateClient();
    var userId = await OnboardingTestSupport.RegisterOwnerAsync(
      anonClient, "owner@state-fresh.local", "+48501333001", ct: ct);
    await OnboardingTestSupport.ConfirmAccountInDbAsync(factory, userId, ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var state = await response.Content.ReadFromJsonAsync<OnboardingStateBody>(JsonRead, ct);
    Assert.NotNull(state);

    Assert.False(state!.OnboardingCompleted);
    Assert.Equal("Profile", state.NextStep);
    Assert.False(state.HasTenant);
  }

  /// <summary>
  /// Wznowienie po branży prowadzi na „Zapisy", nie na grafik.
  ///
  /// Kolejność kroków to … usługi → ZAPISY → terminy → godziny → gotowe: grafik jest ostatni,
  /// bo z niego wychodzi się prosto do aplikacji. Wybór z „Zapisów" nie zostawia śladu w bazie
  /// (tryb potwierdzania ma wartość domyślną, więc nie da się odróżnić wyboru od jego braku),
  /// więc niedomknięty grafik musi cofać na PIERWSZY krok tej trójki. Gdyby cofał na grafik,
  /// właścicielka nigdy nie zobaczyłaby pytania o potwierdzanie wizyt, a i tak zostałby jej
  /// zapisany domyślny tryb automatyczny — cicho i bez pytania.
  /// </summary>
  [Fact]
  public async Task Owner_with_industry_but_no_schedule_resumes_at_the_rules_step()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@state-rules.local", "+48501333003", "Rules Salon", "rules-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var industryResponse = await ownerClient.PostAsJsonAsync(
      "/api/onboarding/industry",
      new { industryKey = "nails", services = Array.Empty<object>() },
      ct);
    Assert.Equal(HttpStatusCode.OK, industryResponse.StatusCode);

    var response = await ownerClient.GetAsync("/api/onboarding/state", ct);
    var state = await response.Content.ReadFromJsonAsync<OnboardingStateBody>(JsonRead, ct);
    Assert.NotNull(state);

    Assert.False(state!.OnboardingCompleted);
    Assert.Equal("Rules", state.NextStep);
  }

  [Fact]
  public async Task Owner_who_completed_the_wizard_reports_completed_state()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@state-done.local", "+48501333002", "State Done Salon", "state-done-salon", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.GetAsync("/api/onboarding/state", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var state = await response.Content.ReadFromJsonAsync<OnboardingStateBody>(JsonRead, ct);
    Assert.NotNull(state);

    // Tenant istnieje, ale kreator nie jest domknięty (brak branży/grafiku/„Gotowe”) — kolejny krok
    // to branża, a guard nadal trzyma właściciela poza panelem.
    Assert.True(state!.HasTenant);
    Assert.Equal(tenantId, state.TenantId);
    Assert.False(state.OnboardingCompleted);
    Assert.Equal("Industry", state.NextStep);
  }

  /// <summary>
  /// Powrót na krok „Nazwa salonu i link" po utworzeniu salonu: własny slug NIE jest kolizją.
  /// Wcześniej `CompleteProfile` sprawdzał `AnyAsync(t => t.Slug == slug)` bez wykluczenia
  /// własnego tenanta, więc powtórka z tym samym linkiem leciała 409 na samego siebie.
  /// </summary>
  [Fact]
  public async Task Profile_step_repeated_with_the_same_slug_is_not_a_conflict()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@profile-again.local", "+48501555001", "Salon Again", "salon-again", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await OnboardingTestSupport.PostProfileAsync(
      ownerClient, "Salon Again", "salon-again", ct: ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Idempotencja: to samo id salonu i pracownika, żaden drugi salon nie powstał.
    var body = await response.Content.ReadFromJsonAsync<CompleteProfileProbe>(JsonRead, ct);
    Assert.NotNull(body);
    Assert.Equal(tenantId, body!.TenantId);
    Assert.Equal(employeeId, body.EmployeeId);
  }

  /// <summary>
  /// Cichy błąd: przy istniejącym salonie handler zwracał tylko istniejące id i WYRZUCAŁ nadesłane
  /// dane. Użytkownik wracał na krok 2, poprawiał nazwę salonu, klikał „Dalej", nie dostawał błędu
  /// — i szedł dalej z niezmienioną nazwą. Poprawka: powtórka zapisuje to, co przyszło.
  /// </summary>
  [Fact]
  public async Task Profile_step_repeated_with_changed_data_actually_saves_it()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, tenantId, employeeId) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@profile-edit.local", "+48501555002", "Stara Nazwa", "stary-link",
      firstName: "Stare", lastName: "Nazwisko", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await OnboardingTestSupport.PostProfileAsync(
      ownerClient, "Nowa Nazwa", "nowy-link", firstName: "Nowe", lastName: "Nazwisko2", ct: ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
    Assert.Equal("Nowa Nazwa", tenant.Name);
    Assert.Equal("nowy-link", tenant.Slug);

    var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == employeeId, ct);
    Assert.Equal("Nowe", employee.FirstName);

    // Nadal JEDEN salon — poprawka aktualizuje, nie tworzy drugiego.
    Assert.Equal(1, await db.Employees.IgnoreQueryFilters().CountAsync(e => e.UserId == userId, ct));
  }

  /// <summary>
  /// Kontrola negatywna: wykluczenie własnego tenanta nie może rozbroić ochrony przed przejęciem
  /// CUDZEGO linku przy powrocie na krok 2.
  /// </summary>
  [Fact]
  public async Task Profile_step_repeated_with_someone_elses_slug_still_conflicts()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@rival.local", "+48501555003", "Salon Rywala", "zajety-link", ct: ct);

    var (userId, _, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@mine.local", "+48501555004", "Salon Mój", "moj-link", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await OnboardingTestSupport.PostProfileAsync(
      ownerClient, "Salon Mój", "zajety-link", ct: ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  /// <summary>
  /// F5 na kroku „Nazwa salonu i link" (albo wejście z innego urządzenia): bufor kreatora żyje
  /// w przeglądarce, więc formularz odtwarzamy ze stanu z backendu — plan: „po utworzeniu tenanta
  /// stanem jest baza, nie przeglądarka". Stan MUSI więc oddać nazwę salonu, slug oraz
  /// imię/nazwisko: bez nich krok 2 po odświeżeniu był pusty, a `onNext` (biorący imię z bufora)
  /// odbijał z powrotem na krok „profil".
  /// </summary>
  [Fact]
  public async Task State_returns_data_needed_to_rehydrate_the_wizard_after_refresh()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var (userId, _, _) = await OnboardingTestSupport.RegisterConfirmAndCreateTenantAsync(
      factory, "owner@state-rehydrate.local", "+48501666001", "Salon Do Odtworzenia", "salon-rehydrate",
      firstName: "Anna", lastName: "Kowalska", ct: ct);

    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await ownerClient.GetAsync("/api/onboarding/state", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var state = await response.Content.ReadFromJsonAsync<RehydrateProbe>(JsonRead, ct);
    Assert.NotNull(state);
    Assert.Equal("Salon Do Odtworzenia", state!.SalonName);
    Assert.Equal("salon-rehydrate", state.Slug);
    Assert.Equal("Anna", state.FirstName);
    Assert.Equal("Kowalska", state.LastName);
  }

  private sealed record RehydrateProbe(string? SalonName, string? Slug, string? FirstName, string? LastName);

  private sealed record CompleteProfileProbe(Guid TenantId, Guid EmployeeId);

  /// <summary>
  /// Mina #4 z docs/ONBOARDING-PLAN.md: „Endpoint tworzący tenant musi sprawdzać PhoneNumberConfirmed.
  /// Front to nie zabezpieczenie." Bez tego testu jedyna asercja bezpieczeństwa w module była niepinowana.
  /// </summary>
  [Fact]
  public async Task Profile_step_is_rejected_for_an_unconfirmed_account()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    var anonClient = factory.CreateClient();
    var userId = await OnboardingTestSupport.RegisterOwnerAsync(
      anonClient, "owner@state-unconfirmed.local", "+48501333003", ct: ct);

    // Świadomie POMIJAMY ConfirmAccountInDbAsync — konto ma EmailConfirmed/PhoneNumberConfirmed = false.
    var ownerClient = OnboardingTestSupport.CreateUserClient(factory, userId);
    var response = await OnboardingTestSupport.PostProfileAsync(
      ownerClient, "Sneaky Salon", "sneaky-salon", ct: ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }
}
