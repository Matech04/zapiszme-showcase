using App.Domain.Aggregates.PromoCodeAggregate;

namespace App.Application.Admin.PromoCodes.Dtos;

public record PromoCodeDto(
    Guid Id,
    string Code,
    PromoCodeKind Kind,
    PromoDiscountType DiscountType,
    decimal DiscountValue,
    int? DurationMonths,
    int? MaxTotalUses,
    int MaxUsesPerTenant,
    int CurrentUses,
    DateTime ValidFrom,
    DateTime? ValidUntil,
    PromoCodeAppliesTo AppliesTo,
    bool IsActive,
    string? Metadata,
    DateTime CreatedAt);

public record PromoCodeRedemptionDto(
    Guid Id,
    Guid TenantId,
    Guid PromoCodeId,
    DateTime RedeemedAt,
    DateTime? ExpiresAt,
    string DiscountSnapshot,
    bool IsActive);
