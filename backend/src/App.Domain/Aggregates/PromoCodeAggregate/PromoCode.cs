using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Domain.Aggregates.PromoCodeAggregate;

/// <summary>
/// Globalny kod promocyjny — widoczny dla wszystkich tenantów. NIE implementuje
/// <c>ITenantEntity</c> i NIE ma query filtra (świadomy wyjątek od reguły izolacji tenantów).
/// Każda zmiana warunków kodu nie wpływa na istniejące <c>PromoCodeRedemption</c>
/// (które trzymają snapshot warunków na moment redempcji).
/// </summary>
public class PromoCode : Entity, IAggregateRoot
{
  public string Code { get; private set; } = string.Empty;
  public PromoCodeKind Kind { get; private set; }
  public PromoDiscountType DiscountType { get; private set; }

  /// <summary>
  /// Znaczenie zależy od <see cref="DiscountType"/>:
  /// <list type="bullet">
  ///   <item><c>PriceOverride</c> — nowa cena bazy w PLN (np. 49.00)</item>
  ///   <item><c>PercentOff</c> — procent zniżki 0–100 (np. 20.00)</item>
  ///   <item><c>FreeMonths</c> — liczba miesięcy gratis (np. 3.00)</item>
  ///   <item><c>TrialExtension</c> — liczba dni dodanych do trialu (np. 30.00)</item>
  /// </list>
  /// </summary>
  public decimal DiscountValue { get; private set; }

  /// <summary>Ile miesięcy obowiązuje rabat (głównie dla PercentOff). null = jednorazowo (FreeMonths/TrialExtension) lub forever (PriceOverride).</summary>
  public int? DurationMonths { get; private set; }

  public int? MaxTotalUses { get; private set; }
  public int MaxUsesPerTenant { get; private set; } = 1;
  public int CurrentUses { get; private set; }

  public DateTime ValidFrom { get; private set; }
  public DateTime? ValidUntil { get; private set; }
  public PromoCodeAppliesTo AppliesTo { get; private set; }
  public bool IsActive { get; private set; } = true;
  public string? Metadata { get; private set; }
  public DateTime CreatedAt { get; private set; }

  private PromoCode() { }

  /// <summary>Normalizacja kodu — UPPERCASE, trim. Lookup zawsze przez tę formę.</summary>
  public static string NormalizeCode(string raw) =>
    (raw ?? string.Empty).Trim().ToUpperInvariant();

  // ── Factory methods ──────────────────────────────────────────────────────────────────

  public static PromoCode CreatePriceOverride(
    string code,
    decimal newBasePriceInZloty,
    PromoCodeKind kind = PromoCodeKind.AdminIssued,
    int? maxTotalUses = null,
    int maxUsesPerTenant = 1,
    DateTime? validFrom = null,
    DateTime? validUntil = null,
    PromoCodeAppliesTo appliesTo = PromoCodeAppliesTo.NewTenantsOnly,
    string? metadata = null)
  {
    if (newBasePriceInZloty < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(newBasePriceInZloty), "Cena nie może być ujemna.");
    }
    return Build(code, kind, PromoDiscountType.PriceOverride, newBasePriceInZloty,
      durationMonths: null, maxTotalUses, maxUsesPerTenant, validFrom, validUntil, appliesTo, metadata);
  }

  public static PromoCode CreatePercentOff(
    string code,
    decimal percent,
    int durationMonths,
    PromoCodeKind kind = PromoCodeKind.Influencer,
    int? maxTotalUses = null,
    int maxUsesPerTenant = 1,
    DateTime? validFrom = null,
    DateTime? validUntil = null,
    PromoCodeAppliesTo appliesTo = PromoCodeAppliesTo.Both,
    string? metadata = null)
  {
    if (percent <= 0 || percent > 100)
    {
      throw new ArgumentOutOfRangeException(nameof(percent), "Procent musi być z zakresu (0, 100].");
    }
    if (durationMonths <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(durationMonths), "DurationMonths musi być > 0.");
    }
    return Build(code, kind, PromoDiscountType.PercentOff, percent,
      durationMonths, maxTotalUses, maxUsesPerTenant, validFrom, validUntil, appliesTo, metadata);
  }

  public static PromoCode CreateFreeMonths(
    string code,
    int months,
    PromoCodeKind kind = PromoCodeKind.Referral,
    int? maxTotalUses = null,
    int maxUsesPerTenant = 1,
    DateTime? validFrom = null,
    DateTime? validUntil = null,
    PromoCodeAppliesTo appliesTo = PromoCodeAppliesTo.Both,
    string? metadata = null)
  {
    if (months <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(months), "Months musi być > 0.");
    }
    return Build(code, kind, PromoDiscountType.FreeMonths, months,
      durationMonths: null, maxTotalUses, maxUsesPerTenant, validFrom, validUntil, appliesTo, metadata);
  }

  public static PromoCode CreateTrialExtension(
    string code,
    int days,
    PromoCodeKind kind = PromoCodeKind.AdminIssued,
    int? maxTotalUses = null,
    int maxUsesPerTenant = 1,
    DateTime? validFrom = null,
    DateTime? validUntil = null,
    string? metadata = null)
  {
    if (days <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(days), "Days musi być > 0.");
    }
    // TrialExtension ma sens TYLKO przy rejestracji nowego tenant'a (tenant ma status Trial).
    return Build(code, kind, PromoDiscountType.TrialExtension, days,
      durationMonths: null, maxTotalUses, maxUsesPerTenant, validFrom, validUntil,
      PromoCodeAppliesTo.NewTenantsOnly, metadata);
  }

  private static PromoCode Build(
    string code,
    PromoCodeKind kind,
    PromoDiscountType discountType,
    decimal value,
    int? durationMonths,
    int? maxTotalUses,
    int maxUsesPerTenant,
    DateTime? validFrom,
    DateTime? validUntil,
    PromoCodeAppliesTo appliesTo,
    string? metadata)
  {
    var normalized = NormalizeCode(code);
    if (string.IsNullOrEmpty(normalized) || normalized.Length is < 3 or > 64)
    {
      throw new ArgumentOutOfRangeException(nameof(code), "Code musi mieć 3–64 znaki.");
    }
    if (maxUsesPerTenant < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(maxUsesPerTenant), "MaxUsesPerTenant >= 1.");
    }
    if (maxTotalUses is < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(maxTotalUses), "MaxTotalUses >= 1 lub null.");
    }

    return new PromoCode
    {
      Id = Guid.NewGuid(),
      Code = normalized,
      Kind = kind,
      DiscountType = discountType,
      DiscountValue = value,
      DurationMonths = durationMonths,
      MaxTotalUses = maxTotalUses,
      MaxUsesPerTenant = maxUsesPerTenant,
      CurrentUses = 0,
      ValidFrom = (validFrom ?? DateTime.UtcNow).ToUniversalTime(),
      ValidUntil = validUntil?.ToUniversalTime(),
      AppliesTo = appliesTo,
      IsActive = true,
      Metadata = metadata,
      CreatedAt = DateTime.UtcNow,
    };
  }

  // ── Domain logic ─────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Powód niedostępności kodu (lub null jeśli można redeemować).
  /// Komunikaty są dla logów / admin UI — endpoint publiczny ma zwracać generic message.
  /// </summary>
  public PromoCodeRedeemability CanBeRedeemed(
    DateTime now,
    int redemptionsByThisTenant,
    SubscriptionStatus tenantStatus,
    bool isNewTenantRegistration)
  {
    if (!IsActive) return PromoCodeRedeemability.Reject("Kod jest nieaktywny.");
    if (now < ValidFrom) return PromoCodeRedeemability.Reject("Kod nie jest jeszcze ważny.");
    if (ValidUntil is { } until && now > until) return PromoCodeRedeemability.Reject("Kod wygasł.");
    if (MaxTotalUses is { } cap && CurrentUses >= cap) return PromoCodeRedeemability.Reject("Kod osiągnął limit użyć.");
    if (redemptionsByThisTenant >= MaxUsesPerTenant) return PromoCodeRedeemability.Reject("Kod został już wykorzystany przez ten salon.");

    if (AppliesTo == PromoCodeAppliesTo.NewTenantsOnly && !isNewTenantRegistration)
    {
      return PromoCodeRedeemability.Reject("Kod jest dostępny tylko podczas rejestracji nowego salonu.");
    }
    if (AppliesTo == PromoCodeAppliesTo.ExistingTenantsOnly && isNewTenantRegistration)
    {
      return PromoCodeRedeemability.Reject("Kod jest dostępny tylko dla istniejących salonów.");
    }

    if (DiscountType == PromoDiscountType.TrialExtension && tenantStatus != SubscriptionStatus.Trial)
    {
      return PromoCodeRedeemability.Reject("Kod wydłużający trial można zrealizować tylko podczas trialu.");
    }

    return PromoCodeRedeemability.Ok();
  }

  public void IncrementUses()
  {
    if (MaxTotalUses is { } cap && CurrentUses >= cap)
    {
      throw new InvalidOperationException("PromoCode reached MaxTotalUses.");
    }
    CurrentUses += 1;
  }

  /// <summary>
  /// Zwalnia jedno użycie kodu (rollback rezerwacji miejsca). Wołane, gdy porzucone konto z odroczonym
  /// kodem jest kasowane przez cleanup lub gdy tenant z redempcją jest hard-kasowany — inaczej miejsce
  /// w <see cref="MaxTotalUses"/> przepada bezpowrotnie. Nie schodzi poniżej zera (idempotentne przy 0).
  /// </summary>
  public void DecrementUses()
  {
    if (CurrentUses > 0)
    {
      CurrentUses -= 1;
    }
  }

  public void Deactivate() => IsActive = false;
  public void Activate() => IsActive = true;
  public void ExtendValidity(DateTime? newValidUntil) => ValidUntil = newValidUntil?.ToUniversalTime();
}

/// <summary>Wynik <see cref="PromoCode.CanBeRedeemed"/>. Result-like — kod używa pattern matchingu.</summary>
public readonly record struct PromoCodeRedeemability(bool IsAllowed, string? Reason)
{
  public static PromoCodeRedeemability Ok() => new(true, null);
  public static PromoCodeRedeemability Reject(string reason) => new(false, reason);
}
