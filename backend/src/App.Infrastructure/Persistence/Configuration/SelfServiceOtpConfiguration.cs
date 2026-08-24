using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class SelfServiceOtpConfiguration : IEntityTypeConfiguration<SelfServiceOtp>
{
  public void Configure(EntityTypeBuilder<SelfServiceOtp> builder)
  {
    builder.ToTable("SelfServiceOtps");

    builder.HasKey(o => o.Id);

    builder.Property(o => o.TenantId).IsRequired();

    builder.Property(o => o.Email).HasMaxLength(254);
    builder.Property(o => o.PhoneE164).HasMaxLength(20);
    builder.Property(o => o.CodeHash).HasMaxLength(200).IsRequired();

    builder.Property(o => o.CreatedAtUtc).IsRequired();
    builder.Property(o => o.ExpiresAtUtc).IsRequired();
    builder.Property(o => o.FailedAttempts).HasDefaultValue(0);
    builder.Property(o => o.Consumed).HasDefaultValue(false);
    builder.Property(o => o.SessionToken);
    builder.Property(o => o.SessionExpiresAtUtc);

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(o => o.TenantId)
      .OnDelete(DeleteBehavior.Cascade);

    builder.HasIndex(o => new { o.TenantId, o.Email });
    builder.HasIndex(o => new { o.TenantId, o.PhoneE164 });
    builder.HasIndex(o => o.ExpiresAtUtc);
    builder.HasIndex(o => o.SessionToken);
  }
}
