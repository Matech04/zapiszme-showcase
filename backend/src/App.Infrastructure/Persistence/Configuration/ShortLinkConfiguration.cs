using App.Domain.Aggregates.ShortLinkAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
  public void Configure(EntityTypeBuilder<ShortLink> builder)
  {
    builder.ToTable("ShortLinks");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
    builder.HasIndex(x => x.Code).IsUnique();

    builder.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2048).IsRequired();
    builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(50);
    builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
    builder.Property(x => x.AppointmentId).HasColumnName("appointment_id").IsRequired(false);
    builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired(false);

    // Brak HasQueryFilter — celowo. Redirect /p/{code} rozwiązuje się anonimowo, bez kontekstu tenanta.
  }
}
