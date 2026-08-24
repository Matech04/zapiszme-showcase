using App.Domain.Aggregates.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class PhoneConfirmationOtpConfiguration : IEntityTypeConfiguration<PhoneConfirmationOtp>
{
  public void Configure(EntityTypeBuilder<PhoneConfirmationOtp> builder)
  {
    builder.ToTable("PhoneConfirmationOtps");

    builder.HasKey(o => o.Id);

    builder.Property(o => o.UserId).IsRequired();
    builder.Property(o => o.CodeHash).HasMaxLength(200).IsRequired();
    builder.Property(o => o.CreatedAt).IsRequired();
    builder.Property(o => o.ExpiresAt).IsRequired();
    builder.Property(o => o.AttemptsCount).HasDefaultValue(0).IsRequired();
    builder.Property(o => o.ConsumedAt);

    builder.HasOne<User>()
      .WithMany()
      .HasForeignKey(o => o.UserId)
      .OnDelete(DeleteBehavior.Cascade);

    // Częsty lookup: aktywny OTP per user. Postgres filtered index (`WHERE consumed_at IS NULL`)
    // dla szybkich query "find active otp for user".
    builder.HasIndex(o => o.UserId)
      .HasFilter("\"ConsumedAt\" IS NULL")
      .HasDatabaseName("ix_phone_confirmation_otps_active_per_user");

    builder.HasIndex(o => o.ExpiresAt);
  }
}
