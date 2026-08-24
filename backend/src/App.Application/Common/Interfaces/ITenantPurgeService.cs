namespace App.Application.Common.Interfaces;

/// <summary>
/// Twardy, kompletny purge całego grafu danych jednego tenanta (salonu): wizyty, klientki,
/// pracownicy, usługi, powiadomienia, zużycie, sesje wsparcia oraz powiązane konta Identity
/// (o ile nie należą do innego, wciąż istniejącego salonu).
///
/// To świadomy wyjątek od soft-delete — używany przez:
/// 1. panel administratora platformy (<c>DeleteTenantCommand</c>),
/// 2. cleanup efemerycznych demo-tenantów (<c>DemoTenantCleanupHostedService</c>).
///
/// Operacja jest cross-tenant i wykonywana poza kontekstem tenanta (admin systemowy bez
/// aktywnej sesji wsparcia → <c>ICurrentTenantService.TenantId == null</c>, przez co write-check
/// <c>TenantViolation</c> w <c>SaveChangesAsync</c> jest świadomie pomijany).
/// </summary>
public interface ITenantPurgeService
{
  /// <param name="deleteOrphanedUsers">
  /// Domyślnie <c>true</c> — hard-delete salonu usuwa też powiązane konta Identity (o ile nie
  /// należą do innego salonu). <c>false</c> kasuje salon, ale ZOSTAWIA konta: używa tego dev-owy
  /// reset onboardingu (<c>POST /api/_dev/reset-onboarding</c>), żeby dało się przejść kreator
  /// ponownie tym samym, już potwierdzonym kontem — bez rejestracji, maila i OTP.
  /// </param>
  Task PurgeAsync(Guid tenantId, CancellationToken ct, bool deleteOrphanedUsers = true);
}
