using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class NotificationUsageRecordConfiguration : IEntityTypeConfiguration<NotificationUsageRecord>
{
  public void Configure(EntityTypeBuilder<NotificationUsageRecord> builder)
  {
    builder.ToTable("NotificationUsage");

    builder.HasKey(u => u.Id);

    builder.Property(u => u.TenantId).IsRequired();
    builder.Property(u => u.Channel).HasConversion<int>().IsRequired();
    builder.Property(u => u.Type).HasConversion<int>().IsRequired();
    builder.Property(u => u.SentAtUtc).IsRequired();
    builder.Property(u => u.Success).IsRequired();
    builder.Property(u => u.Points).HasColumnType("numeric(10,2)").IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(u => u.TenantId)
      .OnDelete(DeleteBehavior.Cascade);

    // Agregacja miesięczna per tenant; kanał w kluczu, bo raport rozbija SMS vs e-mail.
    builder.HasIndex(u => new { u.TenantId, u.SentAtUtc });
    builder.HasIndex(u => new { u.TenantId, u.Channel, u.SentAtUtc });
  }
}
