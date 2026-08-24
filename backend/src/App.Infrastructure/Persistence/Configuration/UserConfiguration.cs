using App.Domain.Aggregates.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
  public void Configure(EntityTypeBuilder<User> builder)
  {
    builder.ToTable("Users");

    builder.Property(u => u.Email)
      .HasMaxLength(255)
      .IsRequired();

    builder.Property(u => u.DisplayName)
      .HasColumnName("display_name")
      .HasMaxLength(200)
      .IsRequired();

    builder.Property(u => u.UserName)
      .HasMaxLength(255);

    builder.Property(u => u.CreatedAt)
      .HasColumnName("created_at")
      .IsRequired();

    builder.Property(u => u.PendingPromoCode)
      .HasColumnName("pending_promo_code")
      .HasMaxLength(64);

    builder.HasIndex(u => new { u.EmailConfirmed, u.CreatedAt })
      .HasDatabaseName("IX_Users_EmailConfirmed_CreatedAt");

    builder.HasIndex(u => u.NormalizedEmail)
      .HasDatabaseName("EmailIndex");

    builder.HasIndex(u => u.NormalizedUserName)
      .HasDatabaseName("UserNameIndex")
      .IsUnique();
  }
}