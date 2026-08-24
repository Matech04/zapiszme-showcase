using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
  public void Configure(EntityTypeBuilder<Notification> builder)
  {
    builder.ToTable("Notifications");

    builder.HasKey(n => n.Id);

    builder.Property(n => n.TenantId).IsRequired();
    builder.Property(n => n.RecipientUserId);
    builder.Property(n => n.Type).HasConversion<int>().IsRequired();
    builder.Property(n => n.Subject).HasMaxLength(300).IsRequired();
    builder.Property(n => n.ShortText).HasMaxLength(500).IsRequired();
    builder.Property(n => n.ReferenceId);
    builder.Property(n => n.CreatedAtUtc).IsRequired();
    builder.Property(n => n.ReadAtUtc);

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(n => n.TenantId)
      .OnDelete(DeleteBehavior.Cascade);

    builder.HasIndex(n => new { n.TenantId, n.CreatedAtUtc });
    builder.HasIndex(n => new { n.TenantId, n.ReadAtUtc });
  }
}
