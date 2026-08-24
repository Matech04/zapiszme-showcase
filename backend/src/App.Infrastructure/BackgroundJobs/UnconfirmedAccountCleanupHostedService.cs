using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo (co 1h) usuwa PORZUCONE self-signupy — rejestracje, w których właściciel nie
/// potwierdził w grace window adresu e-mail lub (jeśli podał numer) kodu SMS. Zwalnia slug /
/// nazwę / numer i nie zostawia sierot w bazie.
///
/// UWAGA — dwa historyczne błędy, świadomie tu domknięte (zob. testy AUTH-CLEANUP):
///
/// 1. Warunek telefonu. Numer telefonu zbiera WYŁĄCZNIE self-signup (register-owner). Konta
///    zakładane przez admina (admin/create-salon) i zaproszenia pracowników (InviteEmployee)
///    nie mają numeru NIGDY, więc <c>!PhoneNumberConfirmed</c> był dla nich zawsze prawdziwy i
///    kwalifikował żywe salony do skasowania. Telefon dyskwalifikuje więc tylko, gdy w ogóle
///    istnieje: <c>PhoneNumber != null &amp;&amp; !PhoneNumberConfirmed</c>.
///
/// 2. Zasięg kasowania. Usunięcie porzuconego konta kasowało KAŻDY tenant, w którym miało ono
///    rekord Employee — także cudzy salon (niepotwierdzone zaproszenie wskazuje na tenant
///    właściciela). Tenant kasujemy więc tylko, gdy jest pustą skorupą świeżego self-signupu
///    (jedyny pracownik = to konto, brak wizyt / klientów / usług). W innym wypadku zostawiamy
///    tenant nietknięty — usunięcie usera i tak odczepia jego Employee (FK user_id = SET NULL).
///
/// Grace window: konfigurowalne przez <c>Auth:UnconfirmedAccountGraceHours</c>
/// (default 48h). Interwał sprawdzania: <c>Auth:UnconfirmedAccountCleanupIntervalMinutes</c>
/// (default 60). W środowisku Testing nie startujemy hosted service (zob. Program.cs).
/// </summary>
public sealed class UnconfirmedAccountCleanupHostedService : BackgroundService
{
  /// <summary>
  /// Rola administratora platformy — konta z nią nigdy nie są kasowane automatycznie.
  /// Ta sama nazwa co w polityce <c>SystemAdminOnly</c> (Program.cs).
  /// </summary>
  internal const string PlatformAdminRole = "Admin";

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<UnconfirmedAccountCleanupHostedService> _logger;
  private readonly TimeSpan _graceWindow;
  private readonly TimeSpan _interval;
  private readonly TimeProvider _timeProvider;

  public UnconfirmedAccountCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<UnconfirmedAccountCleanupHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var graceHours = configuration.GetValue<double?>("Auth:UnconfirmedAccountGraceHours") ?? 48d;
    var intervalMinutes = configuration.GetValue<double?>("Auth:UnconfirmedAccountCleanupIntervalMinutes") ?? 60d;
    _graceWindow = TimeSpan.FromHours(graceHours);
    _interval = TimeSpan.FromMinutes(intervalMinutes);
  }

  /// <summary>
  /// E2E backdoor entry point — wymusza jeden cykl bez czekania na interwał.
  /// </summary>
  internal Task ExecuteOnceAsync(CancellationToken ct) => RunCycleAsync(ct);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunCycleAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Cleanup niepotwierdzonych kont — błąd cyklu.");
      }

      try
      {
        await Task.Delay(_interval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  internal async Task RunCycleAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _graceWindow, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: nie czeka, nie tworzy własnego scope ani logger-a — wszystko
  /// wstrzykiwane z zewnątrz. Wykonuje JEDEN cykl czyszczenia.
  /// </summary>
  internal static async Task RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    TimeSpan graceWindow,
    CancellationToken ct,
    ILogger logger)
  {
    var cutoff = utcNow - graceWindow;

    // Podzapytanie: userzy, którzy MAJĄ jakikolwiek rekord Employee (czyli tenant już powstał).
    // IgnoreQueryFilters, bo cleanup działa bez kontekstu tenanta. Komponuje się do NOT IN/EXISTS.
    var usersWithEmployee = db.Employees
      .IgnoreQueryFilters()
      .Where(e => e.UserId != null)
      .Select(e => e.UserId!.Value);

    // Konta administratorów platformy są WYŁĄCZONE z automatycznego kasowania — bezwarunkowo,
    // dla obu kryteriów.
    //
    // Kryterium #2 zakładało, że admina chroni brak potwierdzonego telefonu. Na produkcji to
    // założenie okazało się fałszywe: konto `kontakt@zapisz.me` ma PhoneNumberConfirmed = true
    // przy PhoneNumber = NULL, więc kwalifikowało się do TWARDEGO skasowania w pierwszym cyklu
    // po starcie API. Admin nie ma rekordu Employee (nie jest pracownikiem żadnego salonu), więc
    // „brak Employee" nigdy nie będzie dla niego sygnałem porzuconego kreatora.
    //
    // Bramka jest na roli, nie na fladze telefonu, bo rola jest niezmiennikiem tego konta —
    // flagi kontaktowe bywają ustawiane przez seed, migracje danych i ścieżki provisioningu,
    // czego ten job nie kontroluje. Świadomie obejmuje też kryterium #1: żaden automat nie ma
    // prawa skasować konta administratora platformy, niezależnie od stanu potwierdzeń.
    var platformAdminIds = db.UserRoles
      .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PlatformAdminRole))
      .Select(ur => ur.UserId);

    var expiredUsers = await db.Users
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(u => u.CreatedAt < cutoff && !platformAdminIds.Contains(u.Id) && (
          // Kryterium #1 — niepotwierdzony self-signup. Telefon dyskwalifikuje tylko gdy został
          // podany (patrz błąd #1 w podsumowaniu klasy).
          (!u.EmailConfirmed || (u.PhoneNumber != null && !u.PhoneNumberConfirmed))
          // Kryterium #2 — sierota kreatora: mail I telefon potwierdzone, ale konto nie ma
          // rekordu Employee, więc tenant nigdy nie powstał (porzucenie na kroku „profil" w
          // Drodze B, gdzie Tenant/Employee tworzy dopiero CompleteProfile). Kryterium #1 takiego
          // konta NIE łapie (obie flagi true). Bez Employee => brak tenanta do skasowania — po
          // prostu usuwamy osierocone konto. Adminów wyklucza jawna bramka na roli powyżej
          // (NIE brak potwierdzonego telefonu — to założenie było nieprawdziwe), a zaproszonych
          // pracowników rekord Employee istniejący od chwili utworzenia zaproszenia.
          || (u.EmailConfirmed && u.PhoneNumberConfirmed && !usersWithEmployee.Contains(u.Id))
        ))
      .Select(u => new { u.Id, u.Email })
      .ToListAsync(ct);

    if (expiredUsers.Count == 0)
    {
      return;
    }

    var deletedTenants = 0;
    var deletedUsers = 0;

    foreach (var expired in expiredUsers)
    {
      try
      {
        var deleted = await DeleteUserAggregateAsync(db, expired.Id, ct);
        if (deleted)
        {
          deletedUsers++;
          deletedTenants++;
        }
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Cleanup niepotwierdzonego konta UserId={UserId} ({Email}) nie powiódł się — pomijam i lecę dalej.",
          expired.Id,
          expired.Email);
      }
    }

    if (deletedUsers > 0)
    {
      logger.LogInformation(
        "Cleanup niepotwierdzonych kont: usunięto {DeletedUsers} userów i {DeletedTenants} tenantów (cutoff={Cutoff:o}).",
        deletedUsers,
        deletedTenants,
        cutoff);
    }
  }

  private static async Task<bool> DeleteUserAggregateAsync(
    ApplicationDbContext db,
    Guid userId,
    CancellationToken ct)
  {
    // Wszystkie tenanty, których "owner-employee" wskazuje na tego usera.
    // Niepotwierdzona rejestracja tworzy 1 tenant + 1 employee, ale defensywnie obsługujemy N.
    var tenantIds = await db.Employees
      .IgnoreQueryFilters()
      .Where(e => e.UserId == userId)
      .Select(e => e.TenantId)
      .Distinct()
      .ToListAsync(ct);

    foreach (var tenantId in tenantIds)
    {
      // Tylko pustą skorupę świeżego self-signupu wolno skasować (patrz błąd #2 w podsumowaniu
      // klasy). Żywy salon — właściciel + niepotwierdzone zaproszenie pracownika, albo tenant
      // z jakimikolwiek danymi — zostaje nietknięty. Employee usuwanego usera i tak zostanie
      // odczepiony przez FK user_id = SET NULL, więc nie tworzymy sieroty.
      if (await IsAbandonedEmptyTenantAsync(db, tenantId, ct))
      {
        await RemoveTenantDataAsync(db, tenantId, ct);
      }
    }

    var user = await db.Users
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null)
    {
      return false;
    }

    var userRoles = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
    if (userRoles.Count > 0)
    {
      db.UserRoles.RemoveRange(userRoles);
    }
    db.Users.Remove(user);
    await db.SaveChangesAsync(ct);
    return true;
  }

  /// <summary>
  /// Czy tenant to pusta skorupa porzuconego self-signupu — dokładnie jeden pracownik i zero
  /// realnych danych (wizyty / klienci / usługi). Tylko taki tenant wolno skasować razem z
  /// porzuconym kontem. Świeża rejestracja tworzy właśnie taki tenant (login jest zablokowany
  /// przed potwierdzeniem, więc właściciel nie zdążył nic dodać). Każde odstępstwo — drugi
  /// pracownik (np. zaproszenie), wizyta, klient, usługa — oznacza żywy salon i blokuje kasowanie.
  /// </summary>
  private static async Task<bool> IsAbandonedEmptyTenantAsync(
    ApplicationDbContext db,
    Guid tenantId,
    CancellationToken ct)
  {
    var employeeCount = await db.Employees.IgnoreQueryFilters()
      .CountAsync(e => e.TenantId == tenantId, ct);
    if (employeeCount != 1) return false;

    var hasAppointments = await db.Appointments.IgnoreQueryFilters()
      .AnyAsync(a => a.TenantId == tenantId, ct);
    if (hasAppointments) return false;

    var hasCustomers = await db.Customers.IgnoreQueryFilters()
      .AnyAsync(c => c.TenantId == tenantId, ct);
    if (hasCustomers) return false;

    var hasServices = await db.Services.IgnoreQueryFilters()
      .AnyAsync(s => s.TenantId == tenantId, ct);
    if (hasServices) return false;

    return true;
  }

  private static async Task RemoveTenantDataAsync(
    ApplicationDbContext db,
    Guid tenantId,
    CancellationToken ct)
  {
    // Kolejność: dzieci → rodzic, żeby OnDelete(Restrict) na FK nie blokowało. Dzieci kasowane
    // przez kaskadę na realnym Postgresie usuwamy jawnie — EF InMemory (testy) kaskad nie
    // egzekwuje, a jawne usunięcie jest nieszkodliwe. Lista musi pokrywać się z TenantPurgeService.

    // Redempcje kodów promo — PRZED usunięciem zwalniamy zarezerwowane użycie na PromoCode,
    // inaczej current_uses zostaje trwale podbite i miejsce w MaxTotalUses przepada (mina #3).
    await ReleasePromoRedemptionsAsync(db, tenantId, ct);

    var notifications = await db.Notifications.IgnoreQueryFilters()
      .Where(n => n.TenantId == tenantId).ToListAsync(ct);
    if (notifications.Count > 0) db.Notifications.RemoveRange(notifications);

    var notificationUsage = await db.NotificationUsage.IgnoreQueryFilters()
      .Where(u => u.TenantId == tenantId).ToListAsync(ct);
    if (notificationUsage.Count > 0) db.NotificationUsage.RemoveRange(notificationUsage);

    var selfServiceOtps = await db.SelfServiceOtps.IgnoreQueryFilters()
      .Where(o => o.TenantId == tenantId).ToListAsync(ct);
    if (selfServiceOtps.Count > 0) db.SelfServiceOtps.RemoveRange(selfServiceOtps);

    var appointments = await db.Appointments.IgnoreQueryFilters()
      .Where(a => a.TenantId == tenantId).ToListAsync(ct);
    if (appointments.Count > 0) db.Appointments.RemoveRange(appointments);

    var customers = await db.Customers.IgnoreQueryFilters()
      .Where(c => c.TenantId == tenantId).ToListAsync(ct);
    if (customers.Count > 0) db.Customers.RemoveRange(customers);

    var employees = await db.Employees.IgnoreQueryFilters()
      .Where(e => e.TenantId == tenantId).ToListAsync(ct);
    if (employees.Count > 0) db.Employees.RemoveRange(employees);

    var services = await db.Services.IgnoreQueryFilters()
      .Where(s => s.TenantId == tenantId).ToListAsync(ct);
    if (services.Count > 0) db.Services.RemoveRange(services);

    var serviceCategories = await db.ServiceCategories.IgnoreQueryFilters()
      .Where(c => c.TenantId == tenantId).ToListAsync(ct);
    if (serviceCategories.Count > 0) db.ServiceCategories.RemoveRange(serviceCategories);

    var vatRates = await db.VatRates.IgnoreQueryFilters()
      .Where(v => v.TenantId == tenantId).ToListAsync(ct);
    if (vatRates.Count > 0) db.VatRates.RemoveRange(vatRates);

    var shiftTemplates = await db.ShiftTemplates.IgnoreQueryFilters()
      .Where(s => s.TenantId == tenantId).ToListAsync(ct);
    if (shiftTemplates.Count > 0) db.ShiftTemplates.RemoveRange(shiftTemplates);

    // Sesje wsparcia (audyt impersonacji) — encja platformowa bez FK do tenanta; po usunięciu
    // salonu wskazywałyby na nieistniejący TargetTenantId, więc czyścimy je razem z salonem.
    var impersonationSessions = await db.ImpersonationSessions.IgnoreQueryFilters()
      .Where(s => s.TargetTenantId == tenantId).ToListAsync(ct);
    if (impersonationSessions.Count > 0) db.ImpersonationSessions.RemoveRange(impersonationSessions);

    var tenant = await db.Tenants.IgnoreQueryFilters()
      .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    if (tenant != null) db.Tenants.Remove(tenant);
  }

  /// <summary>
  /// Zwalnia (DecrementUses) po jednym użyciu za każdą redempcję kodu tego tenanta i usuwa jej
  /// rekordy. Wołane przed hard-delete salonu, inaczej <c>promo_codes.current_uses</c> zostaje
  /// trwale podbite (mina #3). Współdzielona intencja z <c>TenantPurgeService</c>.
  /// </summary>
  private static async Task ReleasePromoRedemptionsAsync(
    ApplicationDbContext db,
    Guid tenantId,
    CancellationToken ct)
  {
    var redemptions = await db.PromoCodeRedemptions.IgnoreQueryFilters()
      .Where(r => r.TenantId == tenantId).ToListAsync(ct);
    if (redemptions.Count == 0)
    {
      return;
    }

    var codeIds = redemptions.Select(r => r.PromoCodeId).Distinct().ToList();
    var codes = await db.PromoCodes.Where(c => codeIds.Contains(c.Id)).ToListAsync(ct);
    foreach (var redemption in redemptions)
    {
      codes.FirstOrDefault(c => c.Id == redemption.PromoCodeId)?.DecrementUses();
    }

    db.PromoCodeRedemptions.RemoveRange(redemptions);
  }
}
