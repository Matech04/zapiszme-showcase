using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
  public void Configure(EntityTypeBuilder<Customer> builder)
  {
    builder.ToTable("Customers");

    builder.HasKey(c => c.Id);

    builder.Property(c => c.FirstName)
      .HasMaxLength(100);


    builder.Property(c => c.LastName)
      .HasMaxLength(100);

    builder.Property(c => c.Email)
      .HasMaxLength(255);

    // PhoneNumber jest nullable — wynika z możliwości zaczepienia klienta po
    // publicznej rezerwacji tylko przez e-mail (`Customer.CreateFromPublicBooking`).
    var phoneConverter = new ValueConverter<PhoneNumber?, string?>(
      phone => phone == null ? null : phone.Value,
      value => string.IsNullOrEmpty(value) ? null : new PhoneNumber(value));

    builder.Property(c => c.PhoneNumber)
      .HasMaxLength(20)
      .IsRequired(false)
      .HasConversion(phoneConverter);

    // Cyfry numeru (bez „+”) do wyszukiwania fragmentem. Utrzymywane w domenie (Customer.SetPhoneNumber),
    // dzięki czemu działa też na providerze InMemory w testach — w przeciwieństwie do kolumny generowanej
    // po stronie Postgresa. LIKE po kolumnie z value converterem (PhoneNumber) nie tłumaczy się na SQL.
    builder.Property(c => c.PhoneNumberSearch)
      .HasMaxLength(20)
      .IsRequired(false);

    builder.Property(c => c.CreatedAt)
      .IsRequired();

    builder.Property(c => c.GeneralNotes)
      .HasMaxLength(1000);

    builder.Property(c => c.InstagramNick)
      .HasColumnName("instagram_nick")
      .HasMaxLength(30)
      .IsRequired(false);

    builder.Property(c => c.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true);

    builder.Property(c => c.Source)
      .HasColumnName("source")
      .HasDefaultValue(CustomerSource.Manual)
      .HasConversion<int>();

    builder.Property(c => c.IsWhitelisted)
      .HasColumnName("is_whitelisted")
      .HasDefaultValue(false);

    builder.Property(c => c.TenantId)
      .IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(c => c.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasIndex(c => new { c.TenantId, c.PhoneNumber });
    builder.HasIndex(c => new { c.TenantId, c.PhoneNumberSearch });
    builder.HasIndex(c => new { c.TenantId, c.Email });
  }
}