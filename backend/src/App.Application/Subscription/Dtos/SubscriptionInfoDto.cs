namespace App.Application.Subscription.Dtos;

/// <summary>
/// DTO statusu subskrypcji salonu. Pola pricing-related (price/sms allowance)
/// liczone on-the-fly z <c>seats</c> + <c>isFoundingMember</c> + ewentualnego rabatu.
/// </summary>
public record SubscriptionInfoDto(
    string Status,
    string EffectiveStatus,
    int Seats,
    bool IsFoundingMember,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    bool IsTrialActive,
    int DaysRemainingInTrial,
    /// <summary>Cena bazowa per-seat bez aplikowanego rabatu (groszowo).</summary>
    int BaseMonthlyPriceInGrosze,
    /// <summary>Cena ostateczna z aplikowanym rabatem (groszowo). Równa bazowej jeśli brak active promo.</summary>
    int MonthlyPriceInGrosze,
    int MonthlySmsAllowance,
    int MonthlySmsUsed,
    /// <summary>Informacja o aktywnym rabacie — null jeśli brak.</summary>
    ActivePromoCodeDto? ActivePromoCode
);

public record ActivePromoCodeDto(
    string Code,
    string DiscountType,
    decimal DiscountValue,
    DateTimeOffset? ExpiresAt
);
