using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class SmsTemplateConfiguration : IEntityTypeConfiguration<SmsTemplate>
{
  public void Configure(EntityTypeBuilder<SmsTemplate> builder)
  {
    builder.ToTable("SmsTemplates");

    builder.HasKey(t => t.Id);

    builder.Property(t => t.TenantId).IsRequired();
    builder.Property(t => t.NotificationType).HasConversion<int>().IsRequired();
    builder.Property(t => t.Status).HasConversion<int>().IsRequired();

    // Treści to plain text; 140 (GSM-7) / 70 (UCS-2) limit egzekwuje walidacja aplikacyjna, ale
    // dajemy zapas w kolumnie na wypadek przyszłej zmiany polityki długości.
    builder.Property(t => t.Body).HasMaxLength(320);
    builder.Property(t => t.PendingBody).HasMaxLength(320);
    builder.Property(t => t.RejectionReason).HasMaxLength(500);

    builder.Property(t => t.SubmittedAtUtc);
    builder.Property(t => t.ReviewedAtUtc);

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(t => t.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    // Jeden szablon na (tenant, typ) — unikalny.
    builder.HasIndex(t => new { t.TenantId, t.NotificationType }).IsUnique();

    // Adminowa kolejka moderacji filtruje po statusie cross-tenant.
    builder.HasIndex(t => t.Status);
  }
}
