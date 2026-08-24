using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class VatRateConfiguration : IEntityTypeConfiguration<VatRate>
{
  public void Configure(EntityTypeBuilder<VatRate> builder)
  {
    builder.ToTable("VatRates");

    builder.HasKey(v => v.Id);

    builder.Property(v => v.Name)
      .HasMaxLength(50)
      .IsRequired();

    builder.Property(v => v.Value)
      .HasPrecision(5, 4)
      .IsRequired();

    builder.Property(v => v.IsDefault)
      .IsRequired();

    builder.Property(v => v.IsActive)
      .IsRequired();

    builder.Property(v => v.TenantId)
      .IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(v => v.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasIndex(v => new { v.TenantId, v.Name }).IsUnique();
  }
}