using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
  public void Configure(EntityTypeBuilder<Service> builder)
  {
    builder.ToTable("Services");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.TenantId)
      .HasColumnName("tenant_id")
      .IsRequired();

    builder.Property(x => x.CategoryId)
      .HasColumnName("category_id")
      .IsRequired(false);

    builder.Property(x => x.VatRateId)
      .HasColumnName("vat_rate_id")
      .IsRequired();

    builder.Property(x => x.Name)
      .HasColumnName("name")
      .HasMaxLength(200)
      .IsRequired();

    // Opis usługi (publiczny katalog). Nullable text — istniejące wiersze backfillują się NULL-em
    // (metadata-only AddColumn w Postgres). Długość pilnuje walidator komendy + invariant domeny.
    builder.Property(x => x.Description)
      .HasColumnName("description")
      .HasMaxLength(Service.MaxDescriptionLength)
      .IsRequired(false);

    builder.OwnsOne(x => x.Price, price =>
    {
      price.Property(p => p.Amount)
        .HasColumnName("price_amount")
        .HasPrecision(18, 2)
        .IsRequired();

      price.Property(p => p.Currency)
        .HasColumnName("price_currency")
        .HasMaxLength(3)
        .IsRequired();
    });

    // Górna granica widełek cenowych — nullable (null = cena stała). Waluta = price_currency.
    builder.Property(x => x.PriceMaxAmount)
      .HasColumnName("price_max_amount")
      .HasPrecision(18, 2)
      .IsRequired(false);

    builder.Property(x => x.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true);

    // Ukrycie ceny w katalogu (per-usługa). NOT NULL, server-side default false —
    // istniejące wiersze backfillują się bez UPDATE (metadata-only AddColumn w Postgres).
    builder.Property(x => x.HidePrice)
      .HasColumnName("hide_price")
      .IsRequired()
      .HasDefaultValue(false);

    // Pozycja przy ręcznym sortowaniu. NOT NULL, server-side default 0.
    builder.Property(x => x.OrderIndex)
      .HasColumnName("order_index")
      .IsRequired()
      .HasDefaultValue(0);

    builder.Property(x => x.DurationInMinutes)
      .HasColumnName("duration_in_minutes")
      .IsRequired();

    builder.Property(x => x.DepositOverrideValue)
      .HasColumnName("deposit_override_value")
      .HasPrecision(18, 2)
      .IsRequired(false);

    // Przedział czasu wykonania (tylko do wyświetlania) — nullable (null = czas stały).
    builder.Property(x => x.DurationMinMinutes)
      .HasColumnName("duration_min_minutes")
      .IsRequired(false);

    builder.Property(x => x.DurationMaxMinutes)
      .HasColumnName("duration_max_minutes")
      .IsRequired(false);

    // Etykieta grupy wariantów (combo): max 1 usługa z grupy w jednej wizycie. Null = brak grupy.
    builder.Property(x => x.ComboGroup)
      .HasColumnName("combo_group")
      .HasMaxLength(100)
      .IsRequired(false);

    // Czy usługa jest dodatkiem (dobieranym do usługi głównej). NOT NULL, server-side default false.
    builder.Property(x => x.IsAddon)
      .HasColumnName("is_addon")
      .IsRequired()
      .HasDefaultValue(false);

    // Dozwolone dodatki usługi głównej (M:N w obrębie Services). Owned collection — ładowana z agregatem.
    builder.OwnsMany(x => x.Addons, a =>
    {
      a.ToTable("ServiceAddons");

      // Id ustawiane w konstruktorze ServiceAddon (Guid.NewGuid()) — analogicznie do EmployeeServices.
      a.Property(x => x.Id).ValueGeneratedNever();
      a.HasKey(x => x.Id);

      a.WithOwner().HasForeignKey(x => x.MainServiceId);

      a.Property(x => x.TenantId)
        .HasColumnName("tenant_id")
        .IsRequired();

      a.Property(x => x.AddonServiceId)
        .HasColumnName("addon_service_id")
        .IsRequired();

      // Zapobiega podpięciu tego samego dodatku do tej samej usługi głównej dwa razy.
      a.HasIndex(x => new { x.TenantId, x.MainServiceId, x.AddonServiceId }).IsUnique();

      // FK do usługi-dodatku — Restrict (usunięcie usługi nie kasuje powiązań po cichu).
      a.HasOne<Service>()
        .WithMany()
        .HasForeignKey(x => x.AddonServiceId)
        .OnDelete(DeleteBehavior.Restrict);

      a.HasOne<Tenant>()
        .WithMany()
        .HasForeignKey(x => x.TenantId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Navigation(x => x.Addons)
      .HasField("_addons")
      .UsePropertyAccessMode(PropertyAccessMode.Field);

    // Galeria zdjęć usługi (max 5). Owned collection — ładowana z agregatem, kasowana kaskadowo z usługą
    // (child usługi, nie cross-aggregate — Cascade jest OK).
    builder.OwnsMany(x => x.Images, img =>
    {
      img.ToTable("ServiceImages");

      // Id ustawiane w konstruktorze ServiceImage (Guid.NewGuid()) — analogicznie do ServiceAddons.
      img.Property(x => x.Id).ValueGeneratedNever();
      img.HasKey(x => x.Id);

      img.WithOwner().HasForeignKey(x => x.ServiceId);

      img.Property(x => x.TenantId)
        .HasColumnName("tenant_id")
        .IsRequired();

      img.Property(x => x.Url)
        .HasColumnName("url")
        .HasMaxLength(2048)
        .IsRequired();

      img.Property(x => x.ThumbnailUrl)
        .HasColumnName("thumbnail_url")
        .HasMaxLength(2048)
        .IsRequired();

      img.Property(x => x.StorageKey)
        .HasColumnName("storage_key")
        .HasMaxLength(512)
        .IsRequired();

      img.Property(x => x.OrderIndex)
        .HasColumnName("order_index")
        .IsRequired();

      // Wyszukiwanie/sortowanie galerii per usługa w obrębie tenanta.
      img.HasIndex(x => new { x.TenantId, x.ServiceId });

      img.HasOne<Tenant>()
        .WithMany()
        .HasForeignKey(x => x.TenantId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Navigation(x => x.Images)
      .HasField("_images")
      .UsePropertyAccessMode(PropertyAccessMode.Field);

    builder.HasOne<ServiceCategory>()
      .WithMany()
      .HasForeignKey(x => x.CategoryId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasOne<VatRate>()
      .WithMany()
      .HasForeignKey(x => x.VatRateId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}