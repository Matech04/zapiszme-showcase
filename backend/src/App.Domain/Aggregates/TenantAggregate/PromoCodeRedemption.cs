using App.Domain.Common;

namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Konkretne użycie kodu promocyjnego przez tenant'a. Tenant-scoped (<see cref="ITenantEntity"/>),
/// czyli MUSI mieć <c>HasQueryFilter</c> w <c>ApplicationDbContext</c>.
///
/// <para>
/// <c>DiscountSnapshot</c> trzyma JSON ze warunkami rabatu na moment redempcji — freezuje cenę
/// niezależnie od późniejszych zmian na <c>PromoCode</c>. Krytyczne dla fair play (admin nie może
/// retroaktywnie zmienić warunków).
/// </para>
/// </summary>
public class PromoCodeRedemption : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid PromoCodeId { get; private set; }
  public DateTime RedeemedAt { get; private set; }
  public DateTime? ExpiresAt { get; private set; }
  public string DiscountSnapshot { get; private set; } = "{}";
  public bool IsActive { get; private set; } = true;

  private PromoCodeRedemption() { }

  public PromoCodeRedemption(
    Guid tenantId,
    Guid promoCodeId,
    DateTime redeemedAt,
    DateTime? expiresAt,
    string discountSnapshot)
  {
    if (tenantId == Guid.Empty) throw new ArgumentException("TenantId required.", nameof(tenantId));
    if (promoCodeId == Guid.Empty) throw new ArgumentException("PromoCodeId required.", nameof(promoCodeId));
    if (string.IsNullOrWhiteSpace(discountSnapshot)) throw new ArgumentException("Snapshot required.", nameof(discountSnapshot));

    Id = Guid.NewGuid();
    TenantId = tenantId;
    PromoCodeId = promoCodeId;
    RedeemedAt = redeemedAt.ToUniversalTime();
    ExpiresAt = expiresAt?.ToUniversalTime();
    DiscountSnapshot = discountSnapshot;
    IsActive = true;
  }

  public void Deactivate() => IsActive = false;
}
