using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
  public void Configure(EntityTypeBuilder<PushSubscription> builder)
  {
    builder.ToTable("PushSubscriptions");

    builder.HasKey(s => s.Id);

    builder.Property(s => s.TenantId).IsRequired();
    builder.Property(s => s.UserId).IsRequired();
    builder.Property(s => s.Endpoint).HasMaxLength(2000).IsRequired();
    builder.Property(s => s.P256dh).HasMaxLength(300).IsRequired();
    builder.Property(s => s.Auth).HasMaxLength(300).IsRequired();
    builder.Property(s => s.CreatedAtUtc).IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(s => s.TenantId)
      .OnDelete(DeleteBehavior.Cascade);

    // Endpoint jest globalnie unikalny (nadaje go push-service). Unikalny indeks pozwala
    // wykryć ponowną subskrypcję i zrobić upsert zamiast duplikatu.
    builder.HasIndex(s => s.Endpoint).IsUnique();
    // Kanał wysyłki pobiera subskrypcje po odbiorcy w obrębie tenanta.
    builder.HasIndex(s => new { s.TenantId, s.UserId });
  }
}
