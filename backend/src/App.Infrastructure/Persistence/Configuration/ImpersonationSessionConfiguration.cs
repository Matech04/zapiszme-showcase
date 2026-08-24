using App.Domain.Aggregates.ImpersonationAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
{
  public void Configure(EntityTypeBuilder<ImpersonationSession> builder)
  {
    builder.ToTable("impersonation_sessions");

    builder.HasKey(s => s.Id);
    builder.Property(s => s.Id).HasColumnName("id");

    builder.Property(s => s.AdminUserId).HasColumnName("admin_user_id").IsRequired();
    builder.Property(s => s.TargetTenantId).HasColumnName("target_tenant_id").IsRequired();

    builder.Property(s => s.Reason)
      .HasColumnName("reason")
      .HasMaxLength(500)
      .IsRequired();

    builder.Property(s => s.IsReadOnly)
      .HasColumnName("is_read_only")
      .HasDefaultValue(true)
      .IsRequired();

    builder.Property(s => s.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
    builder.Property(s => s.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
    builder.Property(s => s.EndedAtUtc).HasColumnName("ended_at_utc");

    builder.Property(s => s.IpAddress)
      .HasColumnName("ip_address")
      .HasMaxLength(64);

    // Audyt po adminie i po tenancie.
    builder.HasIndex(s => s.AdminUserId);
    builder.HasIndex(s => s.TargetTenantId);
    // Szybkie wyszukanie aktywnych sesji (middleware sprawdza po każdym żądaniu admina).
    builder.HasIndex(s => new { s.EndedAtUtc, s.ExpiresAtUtc });
  }
}
