using App.Domain.Aggregates.PromoCodeAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
  public void Configure(EntityTypeBuilder<PromoCode> builder)
  {
    builder.ToTable("promo_codes");

    builder.HasKey(p => p.Id);
    builder.Property(p => p.Id).HasColumnName("id");

    builder.Property(p => p.Code)
      .HasColumnName("code")
      .HasMaxLength(64)
      .IsRequired();
    // Unikalność po znormalizowanej (UPPER) formie. Domain zawsze zapisuje przez NormalizeCode().
    builder.HasIndex(p => p.Code).IsUnique();

    builder.Property(p => p.Kind)
      .HasColumnName("kind")
      .HasConversion<int>()
      .IsRequired();

    builder.Property(p => p.DiscountType)
      .HasColumnName("discount_type")
      .HasConversion<int>()
      .IsRequired();

    builder.Property(p => p.DiscountValue)
      .HasColumnName("discount_value")
      .HasPrecision(18, 4)
      .IsRequired();

    builder.Property(p => p.DurationMonths).HasColumnName("duration_months");
    builder.Property(p => p.MaxTotalUses).HasColumnName("max_total_uses");
    builder.Property(p => p.MaxUsesPerTenant)
      .HasColumnName("max_uses_per_tenant")
      .HasDefaultValue(1)
      .IsRequired();
    builder.Property(p => p.CurrentUses)
      .HasColumnName("current_uses")
      .HasDefaultValue(0)
      .IsRequired();

    builder.Property(p => p.ValidFrom).HasColumnName("valid_from").IsRequired();
    builder.Property(p => p.ValidUntil).HasColumnName("valid_until");

    builder.Property(p => p.AppliesTo)
      .HasColumnName("applies_to")
      .HasConversion<int>()
      .IsRequired();

    builder.Property(p => p.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true)
      .IsRequired();
    builder.HasIndex(p => p.IsActive);

    builder.Property(p => p.Metadata)
      .HasColumnName("metadata")
      .HasColumnType("jsonb");

    builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
  }
}
