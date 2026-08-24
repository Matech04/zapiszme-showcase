using App.Application.Booking.PromoCodes;
using App.Application.Common.Interfaces;
using App.Application.Subscription.PromoPricing;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Services;

/// <summary>
/// Implementacja atomowego redemption. Race condition na <c>MaxTotalUses</c> rozwiązany przez
/// conditional UPDATE: SQL inkrementuje <c>current_uses</c> tylko jeśli wciąż mieści się w limicie,
/// rows-affected = 0 oznacza że ktoś inny zdążył pierwszy → zwracamy null.
/// </summary>
internal sealed class PromoCodeRedemptionService : IPromoCodeRedemptionService
{
  private readonly IApplicationDbContext _context;

  public PromoCodeRedemptionService(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<PromoCodeRedemption?> RedeemForNewTenantAsync(
    string code,
    Tenant tenant,
    CancellationToken ct)
  {
    var normalized = PromoCode.NormalizeCode(code ?? string.Empty);
    if (string.IsNullOrEmpty(normalized)) return null;

    var dbContext = (DbContext)_context;
    var isRelational = dbContext.Database.IsRelational();

    // Pobieramy code BEZ trackingu (relational) — race-safe inkrement przez raw SQL, change-tracker
    // EF nie może próbować nadpisać CurrentUses. Dla InMemory ładujemy z trackingiem,
    // bo używamy klasycznej ścieżki IncrementUses + SaveChanges (race i tak nie istnieje in-process).
    var query = _context.PromoCodes.AsQueryable();
    if (isRelational)
    {
      query = query.AsNoTracking();
    }
    var promoCode = await query.FirstOrDefaultAsync(p => p.Code == normalized, ct);

    if (promoCode is null) return null;

    var verdict = promoCode.CanBeRedeemed(
      now: DateTime.UtcNow,
      redemptionsByThisTenant: 0,
      tenantStatus: tenant.Subscription.Status,
      isNewTenantRegistration: true);

    if (!verdict.IsAllowed) return null;

    if (isRelational)
    {
      // Conditional UPDATE — atomic increment z guardem na MaxTotalUses. Jeśli równolegle
      // ktoś już zużył ostatnie miejsce, rows-affected = 0 i wycofujemy się.
      var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE promo_codes
        SET current_uses = current_uses + 1
        WHERE id = {promoCode.Id}
          AND is_active = TRUE
          AND (max_total_uses IS NULL OR current_uses < max_total_uses)
      ", ct);

      if (rowsAffected == 0) return null;
    }
    else
    {
      // Provider in-memory — change-tracker increment. Single-threaded testowy kontekst,
      // brak race condition. Walidacja CanBeRedeemed już sprawdziła limit.
      promoCode.IncrementUses();
    }

    var snapshot = DiscountSnapshot.FromCode(promoCode);
    var redemption = new PromoCodeRedemption(
      tenantId: tenant.Id,
      promoCodeId: promoCode.Id,
      redeemedAt: DateTime.UtcNow,
      expiresAt: CalculateExpiry(promoCode),
      discountSnapshot: snapshot.Serialize());

    _context.PromoCodeRedemptions.Add(redemption);

    // Apply efekt do subskrypcji.
    tenant.Subscription.ApplyPromoRedemption(redemption.Id);
    switch (promoCode.DiscountType)
    {
      case PromoDiscountType.FreeMonths:
        tenant.Subscription.ExtendCurrentPeriod((int)promoCode.DiscountValue);
        break;
      case PromoDiscountType.TrialExtension:
        tenant.Subscription.ExtendTrial((int)promoCode.DiscountValue);
        break;
    }

    return redemption;
  }

  private static DateTime? CalculateExpiry(PromoCode code) => code.DiscountType switch
  {
    // PriceOverride trwa tyle, ile DurationMonths (null = forever).
    PromoDiscountType.PriceOverride when code.DurationMonths is { } months =>
      DateTime.UtcNow.AddMonths(months),
    PromoDiscountType.PriceOverride => null,

    // PercentOff trwa zawsze N miesięcy (validator wymusza).
    PromoDiscountType.PercentOff when code.DurationMonths is { } months =>
      DateTime.UtcNow.AddMonths(months),
    PromoDiscountType.PercentOff => DateTime.UtcNow.AddMonths(1),

    // FreeMonths / TrialExtension to jednorazowe akcje — sam record nie wygasa, ale "expires"
    // odpowiada momentowi gdy efekt znika (cena i tak nie była zmodyfikowana).
    PromoDiscountType.FreeMonths or PromoDiscountType.TrialExtension => null,
    _ => null,
  };
}
