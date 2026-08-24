using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Onboarding.Queries.GetOnboardingState;

/// <summary>
/// Stan kreatora onboardingu dla danego usera — punkt wznowienia. Front używa <see cref="NextStep"/>
/// do przekierowania w <c>/setup/**</c>. Booleany pozwalają na finezyjniejsze sterowanie ekranami.
/// </summary>
/// <param name="FirstName">Imię z rekordu Employee — null przed krokiem „profil”.</param>
/// <param name="LastName">Nazwisko z rekordu Employee — null przed krokiem „profil”.</param>
/// <param name="SalonName">
/// Nazwa salonu. Razem z <paramref name="Slug"/>, <paramref name="FirstName"/> i
/// <paramref name="LastName"/> pozwala odtworzyć formularze kroków 1–2 po odświeżeniu strony
/// albo na innym urządzeniu — bufor kreatora żyje w localStorage, więc F5 w innej przeglądarce
/// zostawiłby puste pola mimo istniejącego salonu. Plan: „po utworzeniu tenanta stanem jest baza,
/// nie przeglądarka”.
/// </param>
public sealed record OnboardingStateDto(
  bool HasTenant,
  bool HasProfile,
  bool HasIndustry,
  int ServicesCount,
  bool HasSchedule,
  bool OnboardingCompleted,
  string NextStep,
  Guid? TenantId,
  string? Slug,
  string? FirstName = null,
  string? LastName = null,
  string? SalonName = null,
  bool UsesAdHocSchedule = false);

/// <summary>
/// Odpowiada zarówno dla usera BEZ tenanta (przed „profilem") jak i po jego utworzeniu — dlatego
/// zwykły <c>IRequestHandler</c> (nie <c>TenantHandler</c>) i lookup po <c>UserId</c> z claima,
/// z <c>IgnoreQueryFilters()</c> (tenant może być jeszcze nierozwiązany).
/// </summary>
public sealed record GetOnboardingStateQuery(Guid UserId) : IRequest<OnboardingStateDto>;

internal sealed class GetOnboardingStateQueryHandler
  : IRequestHandler<GetOnboardingStateQuery, OnboardingStateDto>
{
  private readonly IApplicationDbContext _context;
  private readonly UserManager<User> _userManager;

  public GetOnboardingStateQueryHandler(IApplicationDbContext context, UserManager<User> userManager)
  {
    _context = context;
    _userManager = userManager;
  }

  public async Task<OnboardingStateDto> Handle(GetOnboardingStateQuery request, CancellationToken ct)
  {
    var employee = await _context.Employees
      .IgnoreQueryFilters()
      .Where(e => e.UserId == request.UserId && e.IsActive)
      .Select(e => new { e.Id, e.TenantId, e.FirstName, e.LastName, e.UsesAdHocSchedule })
      .FirstOrDefaultAsync(ct);

    // Admin systemowy nie ma rekordu Employee (DbSeeder / EnsureProductionBootstrapAsync tworzą mu
    // tylko User + UserRole) i nigdy nie zakłada salonu. Bez tego wyjątku wpadałby w gałąź „Profile",
    // a onboardingGuard odbijałby go z /admin/system/** do /setup — pętla bez wyjścia, bo tenanta
    // nie stworzy (CompleteProfile wymaga potwierdzonego telefonu, którego admin nie ma).
    // Kreator dotyczy wyłącznie właściciela, więc dla admina onboarding jest „niedotyczący" = ukończony.
    if (employee == null && await IsSystemAdminAsync(request.UserId))
    {
      return new OnboardingStateDto(
        HasTenant: false,
        HasProfile: false,
        HasIndustry: false,
        ServicesCount: 0,
        HasSchedule: false,
        OnboardingCompleted: true,
        NextStep: "Completed",
        TenantId: null,
        Slug: null);
    }

    if (employee == null)
    {
      // BYŁY PRACOWNIK vs ŚWIEŻY WŁAŚCICIEL. Oba przypadki wyglądają tu identycznie — brak
      // AKTYWNEGO rekordu Employee — a znaczą coś zupełnie innego. Zdezaktywowana pracownica ma
      // wciąż konto User z rolą Employee, więc bez tego rozróżnienia guard /admin/** odbijał ją do
      // kreatora ZAKŁADANIA SALONU: absurd komunikacyjny (zwolniona osoba dostaje ekran „załóż
      // własny salon") zakończony ślepą uliczką, bo mutacje kreatora wymagają BusinessManagement,
      // a `CompleteProfile` dodatkowo potwierdzonego telefonu. Kiedyś była pracownica NIE jest
      // w kreatorze — jej konto po prostu straciło dostęp.
      var hadEmployeeRecord = await _context.Employees
        .IgnoreQueryFilters()
        .AnyAsync(e => e.UserId == request.UserId, ct);

      if (hadEmployeeRecord)
      {
        return new OnboardingStateDto(
          HasTenant: false,
          HasProfile: false,
          HasIndustry: false,
          ServicesCount: 0,
          HasSchedule: false,
          OnboardingCompleted: false,
          NextStep: "InactiveAccount",
          TenantId: null,
          Slug: null);
      }

      // Przed krokiem „profil" nie ma tenanta ani pracownika — jedyny sensowny następny krok to profil.
      return new OnboardingStateDto(
        HasTenant: false,
        HasProfile: false,
        HasIndustry: false,
        ServicesCount: 0,
        HasSchedule: false,
        OnboardingCompleted: false,
        NextStep: "Profile",
        TenantId: null,
        Slug: null);
    }

    var tenant = await _context.Tenants
      .IgnoreQueryFilters()
      .Where(t => t.Id == employee.TenantId)
      .Select(t => new { t.Id, t.Name, t.Slug, t.Industry, t.OnboardingCompletedAt })
      .FirstOrDefaultAsync(ct);

    var hasIndustry = tenant != null && !string.IsNullOrEmpty(tenant.Industry);
    var onboardingCompleted = tenant?.OnboardingCompletedAt != null;

    var servicesCount = await _context.Services
      .IgnoreQueryFilters()
      .CountAsync(s => s.TenantId == employee.TenantId && s.IsActive, ct);

    var hasSchedule = await _context.Employees
      .IgnoreQueryFilters()
      .Where(e => e.Id == employee.Id)
      .AnyAsync(e => e.Schedules.Any(), ct);

    // Krok grafiku jest domknięty na DWA sposoby: grafikiem powtarzalnym albo świadomą deklaracją
    // „ustawiam dni na bieżąco" (UsesAdHocSchedule). Bez tego drugiego warunku właściciel, który
    // wybrał papierowy kalendarz, wracałby na krok grafiku w nieskończoność — a to jedyny krok
    // kreatora bez wyjścia. Sam brak grafiku NIE odróżnia „jeszcze nie ustawiłam" od „nie prowadzę".
    var scheduleResolved = hasSchedule || employee.UsesAdHocSchedule;

    // Kolejność wznawiania idzie za kolejnością kroków kreatora: … usługi → ZAPISY → terminy →
    // godziny → gotowe. Wybór z „Zapisów" nie zostawia śladu w bazie (tryb potwierdzania ma wartość
    // domyślną, więc nie da się odróżnić „wybrała Automatyczne" od „nigdy nie pytana"), dlatego
    // niedomknięty grafik cofa na PIERWSZY z tych trzech kroków, a nie na sam grafik. Kto zasady już
    // przeszedł, przeklika je drugi raz jednym „Dalej" — to tańsze niż kolumna-znacznik w bazie.
    string nextStep;
    if (!hasIndustry)
    {
      nextStep = "Industry";
    }
    else if (!scheduleResolved)
    {
      nextStep = "Rules";
    }
    else if (!onboardingCompleted)
    {
      // Grafik zapisany, ale `complete()` nie doszło (błąd sieci na ostatnim przycisku) — wracamy
      // na sam grafik, żeby dało się domknąć kreator bez przechodzenia go od nowa.
      nextStep = "Schedule";
    }
    else
    {
      nextStep = "Completed";
    }

    return new OnboardingStateDto(
      HasTenant: true,
      HasProfile: true,
      HasIndustry: hasIndustry,
      ServicesCount: servicesCount,
      HasSchedule: hasSchedule,
      OnboardingCompleted: onboardingCompleted,
      NextStep: nextStep,
      TenantId: employee.TenantId,
      Slug: tenant?.Slug,
      FirstName: employee.FirstName,
      LastName: employee.LastName,
      SalonName: tenant?.Name,
      UsesAdHocSchedule: employee.UsesAdHocSchedule);
  }

  private async Task<bool> IsSystemAdminAsync(Guid userId)
  {
    var user = await _userManager.FindByIdAsync(userId.ToString());
    return user != null && await _userManager.IsInRoleAsync(user, Roles.Admin);
  }
}
