using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class PromoCodeRedemptionConfiguration : IEntityTypeConfiguration<PromoCodeRedemption>
{
  public void Configure(EntityTypeBuilder<PromoCodeRedemption> builder)
  {
    builder.ToTable("promo_code_redemptions");

    builder.HasKey(r => r.Id);
    builder.Property(r => r.Id).HasColumnName("id");

    builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
    builder.Property(r => r.PromoCodeId).HasColumnName("promo_code_id").IsRequired();
    builder.Property(r => r.RedeemedAt).HasColumnName("redeemed_at").IsRequired();
    builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
    builder.Property(r => r.DiscountSnapshot)
      .HasColumnName("discount_snapshot")
      .HasColumnType("jsonb")
      .IsRequired();
    builder.Property(r => r.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true)
      .IsRequired();

    builder.HasIndex(r => new { r.TenantId, r.IsActive });
    builder.HasIndex(r => r.PromoCodeId);

    // FK do PromoCode → Restrict (usunięcie kodu nie wyzeruje redemptions; history matters).
    builder.HasOne<PromoCode>()
      .WithMany()
      .HasForeignKey(r => r.PromoCodeId)
      .OnDelete(DeleteBehavior.Restrict);

    // FK do Tenant → Cascade (usunięcie tenant'a usuwa jego redemptions).
    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(r => r.TenantId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
