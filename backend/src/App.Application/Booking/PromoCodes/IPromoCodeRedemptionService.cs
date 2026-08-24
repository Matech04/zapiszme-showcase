using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Booking.PromoCodes;

/// <summary>
/// Atomowy redemption kodu promocyjnego dla nowego tenant'a podczas rejestracji.
/// Implementacja musi działać w ramach OTWARTEJ transakcji wywołującego (controller / handler
/// rejestracji) — nie commituje sam. Zapewnia, że limit <c>MaxTotalUses</c> nie zostanie
/// przekroczony nawet przy równoczesnych rejestracjach (conditional UPDATE).
/// </summary>
public interface IPromoCodeRedemptionService
{
  /// <summary>
  /// Próba zaaplikowania kodu do nowo tworzonego tenant'a. Zwraca <c>null</c> jeśli kod nie istnieje,
  /// nieaktywny, lub limit globalny / tenantowy został wyczerpany w międzyczasie (race-safe).
  /// Po sukcesie <c>tenant.Subscription</c> ma ustawiony <c>ActivePromoCodeRedemptionId</c> oraz
  /// (dla FreeMonths/TrialExtension) wydłużone okresy.
  /// </summary>
  Task<PromoCodeRedemption?> RedeemForNewTenantAsync(
    string code,
    Tenant tenant,
    CancellationToken ct);
}
