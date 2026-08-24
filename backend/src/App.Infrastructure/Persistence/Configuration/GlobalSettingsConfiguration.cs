using App.Domain.Aggregates.GlobalSettingsAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class GlobalSettingsConfiguration : IEntityTypeConfiguration<GlobalSettings>
{
  public void Configure(EntityTypeBuilder<GlobalSettings> builder)
  {
    builder.ToTable("global_settings");

    builder.HasKey(g => g.Id);
    builder.Property(g => g.Id).HasColumnName("id");

    builder.Property(g => g.MaintenanceEnabled)
      .HasColumnName("maintenance_enabled")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(g => g.MaintenanceMessage)
      .HasColumnName("maintenance_message")
      .HasMaxLength(GlobalSettings.MaintenanceMessageMaxLength)
      .IsRequired(false);

    builder.Property(g => g.MaintenanceStartedAtUtc)
      .HasColumnName("maintenance_started_at_utc")
      .IsRequired(false);
  }
}
