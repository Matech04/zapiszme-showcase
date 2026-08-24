using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Implementacja <see cref="ITenantPurgeService"/>. Jedno źródło prawdy dla pełnego hard-delete
/// salonu — współdzielone przez panel admina i cleanup demo. Cała operacja to jedno
/// <c>SaveChangesAsync</c> = jedna transakcja (all-or-nothing).
/// </summary>
public sealed class TenantPurgeService : ITenantPurgeService
{
  private readonly ApplicationDbContext _db;

  public TenantPurgeService(ApplicationDbContext db) => _db = db;

  public Task PurgeAsync(Guid tenantId, CancellationToken ct, bool deleteOrphanedUsers = true)
    => PurgeCoreAsync(_db, tenantId, ct, deleteOrphanedUsers);

  /// <summary>
  /// Statyczny rdzeń — testowalny i wywoływany bez DI (np. z hosted service'u cleanup demo,
  /// który ma już własny scope + <see cref="ApplicationDbContext"/>).
  /// </summary>
  internal static async Task PurgeCoreAsync(
    ApplicationDbContext db,
    Guid tenantId,
    CancellationToken ct,
    bool deleteOrphanedUsers = true)
  {
    // Konta Identity powiązane z tym salonem (właściciel + pracownicy). Kasujemy je na końcu,
    // ale tylko jeśli nie są przypisane do innego, wciąż istniejącego salonu.
    var userIds = await db.Employees.IgnoreQueryFilters()
      .Where(e => e.TenantId == tenantId && e.UserId != null)
      .Select(e => e.UserId!.Value)
      .Distinct()
      .ToListAsync(ct);

    // Kolejność: dzieci → rodzic, żeby FK OnDelete(Restrict) nie blokował usunięcia.
    // (Cascade-dzieci jak Notifications/SelfServiceOtp kasujemy jawnie — EF InMemory w testach
    //  nie egzekwuje kaskad, a na realnym Postgresie jawne usunięcie jest nieszkodliwe.)
    await RemoveWhereAsync(db.Notifications, n => n.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.NotificationUsage, u => u.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.SelfServiceOtps, o => o.TenantId == tenantId, ct);
    // PRZED usunięciem redempcji zwalniamy zarezerwowane użycie na każdym PromoCode — inaczej
    // current_uses zostaje trwale podbite i miejsce w MaxTotalUses przepada (mina #3).
    await ReleasePromoRedemptionsAsync(db, tenantId, ct);
    await RemoveWhereAsync(db.Appointments, a => a.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.Customers, c => c.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.Employees, e => e.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.Services, s => s.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.ServiceCategories, c => c.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.VatRates, v => v.TenantId == tenantId, ct);
    await RemoveWhereAsync(db.ShiftTemplates, s => s.TenantId == tenantId, ct);

    // Sesje wsparcia (audyt impersonacji) — encja platformowa, bez FK do tenanta. Po usunięciu
    // salonu wskazywałyby na nieistniejący TargetTenantId, więc czyścimy je razem z salonem.
    await RemoveWhereAsync(db.ImpersonationSessions, s => s.TargetTenantId == tenantId, ct);

    var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    if (tenant != null) db.Tenants.Remove(tenant);

    // deleteOrphanedUsers=false → dev-owy reset onboardingu: salon znika, konto właściciela zostaje
    // (potwierdzony e-mail + telefon), więc kreator da się przejść ponownie bez OTP i rejestracji.
    if (deleteOrphanedUsers && userIds.Count > 0)
    {
      // Nie kasujemy konta, które jest pracownikiem także w innym (pozostającym) salonie.
      var sharedUserIds = await db.Employees.IgnoreQueryFilters()
        .Where(e => e.TenantId != tenantId && e.UserId != null && userIds.Contains(e.UserId!.Value))
        .Select(e => e.UserId!.Value)
        .Distinct()
        .ToListAsync(ct);

      var deletableUserIds = userIds.Where(id => !sharedUserIds.Contains(id)).ToList();

      if (deletableUserIds.Count > 0)
      {
        var roles = await db.UserRoles.Where(ur => deletableUserIds.Contains(ur.UserId)).ToListAsync(ct);
        if (roles.Count > 0) db.UserRoles.RemoveRange(roles);

        var users = await db.Users.Where(u => deletableUserIds.Contains(u.Id)).ToListAsync(ct);
        if (users.Count > 0) db.Users.RemoveRange(users);
      }
    }

    await db.SaveChangesAsync(ct);
  }

  private static async Task RemoveWhereAsync<T>(
    DbSet<T> set,
    System.Linq.Expressions.Expression<Func<T, bool>> predicate,
    CancellationToken ct) where T : class
  {
    var items = await set.IgnoreQueryFilters().Where(predicate).ToListAsync(ct);
    if (items.Count > 0) set.RemoveRange(items);
  }

  /// <summary>
  /// Zwalnia (DecrementUses) po jednym użyciu za każdą redempcję kodu tego tenanta i usuwa jej
  /// rekordy. Wołane przed hard-delete salonu, inaczej <c>promo_codes.current_uses</c> zostaje
  /// trwale podbite (mina #3).
  /// </summary>
  private static async Task ReleasePromoRedemptionsAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
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
