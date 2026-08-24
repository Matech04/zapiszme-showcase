using System.Security.Claims;
using App.Application.Common.Interfaces;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

/// <summary>
/// Narzędzia do RĘCZNEGO testowania w dev. AKTYWNE TYLKO w <c>ASPNETCORE_ENVIRONMENT=Development</c>
/// — każda akcja woła <see cref="GuardEnvironment"/> i poza dev zwraca 404. Nie pojawia się
/// w Swaggerze (<c>IgnoreApi</c>).
///
/// Gate to <c>IsDevelopment()</c>, a NIE <c>!IsProduction()</c>: pod LOCAL_PROD
/// <c>IsProduction()</c> jest <c>true</c>, ale samo LOCAL_PROD to config-flaga doklejana do
/// env Production — gdyby ktoś ustawił ją na prawdziwym prodzie, <c>!IsProduction()</c> nadal
/// dawałoby 404, ale <c>IsDevelopment()</c> jest po prostu węższe i nie zależy od LOCAL_PROD.
/// Wzorzec 1:1 z <c>E2eController</c> (tam gate = <c>IsEnvironment("E2E")</c>).
/// </summary>
[ApiController]
[Route("api/_dev")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevController : ControllerBase
{
  private readonly IHostEnvironment _env;
  private readonly ApplicationDbContext _db;
  private readonly ITenantPurgeService _purge;
  private readonly ILogger<DevController> _logger;

  public DevController(
    IHostEnvironment env,
    ApplicationDbContext db,
    ITenantPurgeService purge,
    ILogger<DevController> logger)
  {
    _env = env;
    _db = db;
    _purge = purge;
    _logger = logger;
  }

  private IActionResult? GuardEnvironment()
  {
    return _env.IsDevelopment() ? null : NotFound();
  }

  /// <summary>
  /// Kasuje salon zalogowanego właściciela (Tenant + Employee + usługi + grafik + ewentualne
  /// wizyty/klientki), ZOSTAWIAJĄC jego konto z potwierdzonym e-mailem i telefonem. Efekt:
  /// <c>GET /api/onboarding/state</c> znów zwraca <c>NextStep=Profile</c>, więc kreator da się
  /// przejść od nowa tym samym kontem — bez rejestracji, linku z maila i kodu OTP.
  ///
  /// Po co: kreator to 8 ekranów, a każde świeże konto kosztuje unikalny e-mail i jedno z
  /// 15 OTP/godzinę na IP (<c>BookingOtpProtection</c>). Reset zdejmuje oba limity z iteracji.
  ///
  /// Uwaga: purge zwalnia redempcję kodu promo (<c>DecrementUses</c>), ale
  /// <c>User.PendingPromoCode</c> został już wyczyszczony przy pierwszym przejściu kreatora —
  /// kolejny przebieg utworzy salon BEZ promo. Do testów promo trzeba świeżej rejestracji.
  /// </summary>
  [Authorize]
  [HttpPost("reset-onboarding")]
  public async Task<IActionResult> ResetOnboarding(CancellationToken ct)
  {
    if (GuardEnvironment() is { } blocked)
    {
      return blocked;
    }

    var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(value, out var userId))
    {
      return Unauthorized();
    }

    var employee = await _db.Employees
      .IgnoreQueryFilters()
      .Where(e => e.UserId == userId)
      .Select(e => new { e.Id, e.TenantId })
      .FirstOrDefaultAsync(ct);

    if (employee == null)
    {
      // Nie ma czego resetować — user jest już przed krokiem „profil". Idempotentnie OK,
      // żeby dało się walić w ten endpoint bez sprawdzania stanu.
      return Ok(new ResetOnboardingResponse(false, null));
    }

    await _purge.PurgeAsync(employee.TenantId, ct, deleteOrphanedUsers: false);

    _logger.LogWarning(
      "[DEV] Reset onboardingu: usunięto salon {TenantId} usera {UserId}; konto zostaje",
      employee.TenantId, userId);

    return Ok(new ResetOnboardingResponse(true, employee.TenantId));
  }
}

/// <param name="Reset">False = nie było czego kasować (user i tak jest przed krokiem „profil").</param>
/// <param name="RemovedTenantId">Id usuniętego salonu albo null.</param>
public sealed record ResetOnboardingResponse(bool Reset, Guid? RemovedTenantId);
