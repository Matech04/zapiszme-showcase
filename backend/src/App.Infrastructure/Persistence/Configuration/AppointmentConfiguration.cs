using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
  public void Configure(EntityTypeBuilder<Appointment> builder)
  {
    builder.ToTable("Appointments");

    builder.HasKey(a => a.Id);

    builder.Property(a => a.Date)
      .IsRequired();

    builder.Property(a => a.StartTime)
      .IsRequired();

    builder.Property(a => a.EndTime)
      .IsRequired();

    // Niestandardowy czas trwania wizyty (override personelu). Null = czas standardowy (suma pozycji).
    builder.Property(a => a.CustomDurationMinutes)
      .HasColumnName("custom_duration_minutes")
      .IsRequired(false);

    // Kolumna w PostgreSQL: varchar(50) (nazwa statusu), zgodnie z migracją BasicAppointments.
    builder.Property(a => a.Status)
      .HasMaxLength(50)
      .HasConversion(
        status => status.Name,
        name => AppointmentStatus.FromName(name))
      .IsRequired();

    builder.OwnsOne(a => a.TotalPrice, price =>
    {
      price.Property(p => p.Amount).HasColumnName("total_price_amount").HasPrecision(18, 2).IsRequired();
      price.Property(p => p.Currency).HasColumnName("total_price_currency").HasMaxLength(3).IsRequired();
    });

    // Cena końcowa — opcjonalny owned type (null = nieustalona). Kolumny nullable, bo nawigacja jest opcjonalna.
    builder.OwnsOne(a => a.FinalPrice, price =>
    {
      price.Property(p => p.Amount).HasColumnName("final_price_amount").HasPrecision(18, 2);
      price.Property(p => p.Currency).HasColumnName("final_price_currency").HasMaxLength(3);
    });

    // Pozycje usług (combo) — owned collection, wzorzec jak EmployeeServices. Czas/cena snapshotowane.
    builder.OwnsMany(a => a.Items, item =>
    {
      item.ToTable("AppointmentServiceItems");

      // Id ustawiane w konstruktorze (Guid.NewGuid()) — ValueGeneratedNever, jak w EmployeeServices.
      item.Property(x => x.Id).ValueGeneratedNever();
      item.HasKey(x => x.Id);

      item.WithOwner().HasForeignKey(x => x.AppointmentId);

      item.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
      item.Property(x => x.ServiceId).HasColumnName("service_id").IsRequired();
      item.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
      item.Property(x => x.Position).HasColumnName("position").IsRequired();
      item.Property(x => x.PriceAmount).HasColumnName("price_amount").HasPrecision(18, 2).IsRequired();
      item.Property(x => x.PriceCurrency).HasColumnName("price_currency").HasMaxLength(3).IsRequired();
      item.Ignore(x => x.Price);

      // FK do Service (Restrict) — pozwala joinować nazwę usługi na żywo i chroni przed kasowaniem usługi z historii.
      item.HasOne<App.Domain.Aggregates.ServiceAggregate.Service>()
        .WithMany()
        .HasForeignKey(x => x.ServiceId)
        .OnDelete(DeleteBehavior.Restrict);

      item.HasIndex(x => new { x.AppointmentId, x.Position }).IsUnique();

      // Indeks na tenant_id — owned collection jest ITenantEntity; query filter / write-check
      // filtruje po TenantId, więc kolumna potrzebuje indeksu jak każda inna tabela tenantowa.
      item.HasIndex(x => x.TenantId);
    });

    // Zdjęcia inspiracji (≤3) — owned collection, wzorzec jak Items. Child wizyty: kasowane
    // kaskadowo razem z wizytą. Trzymamy tylko URL-e + klucz storage (obraz już przetworzony/wgrany).
    builder.OwnsMany(a => a.InspirationImages, img =>
    {
      img.ToTable("AppointmentInspirationImages");

      img.Property(x => x.Id).ValueGeneratedNever();
      img.HasKey(x => x.Id);

      img.WithOwner().HasForeignKey(x => x.AppointmentId);

      img.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
      img.Property(x => x.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
      img.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url").HasMaxLength(2048).IsRequired();
      img.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
      img.Property(x => x.ThumbnailStorageKey).HasColumnName("thumbnail_storage_key").HasMaxLength(512).IsRequired();

      // Indeks na tenant_id — owned collection jest ITenantEntity (jak AppointmentServiceItems).
      img.HasIndex(x => x.TenantId);
      // Lookup zdjęć po wizycie (panel szczegółów wizyty).
      img.HasIndex(x => x.AppointmentId);
    });

    builder.Property(a => a.AppointmentNotes)
      .HasMaxLength(1000);

    builder.OwnsOne(a => a.Lease, lease =>
    {
      lease.Property(l => l.ReservationToken).HasColumnName("lease_reservation_token");
      lease.Property(l => l.ExpiryTimeUtc).HasColumnName("lease_expiry_utc");
    });

    builder.OwnsOne(
      a => a.OtpVerification,
      otp =>
      {
        otp.Property(o => o.Channel)
          .HasColumnName("otp_channel")
          .IsRequired();
        otp.Property(o => o.PhoneE164)
          .HasMaxLength(20)
          .HasColumnName("otp_phone_e164");
        otp.Property(o => o.Email)
          .HasMaxLength(254)
          .HasColumnName("otp_email");
        otp.Property(o => o.OtpHash)
          .HasMaxLength(200)
          .HasColumnName("otp_hash");
        otp.Property(o => o.ExpiryTimeUtc).HasColumnName("otp_expiry_utc");
      });

    builder.Property(a => a.TenantId)
      .IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(a => a.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasOne<Employee>()
      .WithMany()
      .HasForeignKey(a => a.EmployeeId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasOne<Service>()
      .WithMany()
      .HasForeignKey(a => a.ServiceId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.HasOne<Customer>()
      .WithMany()
      .HasForeignKey(a => a.CustomerId)
      .IsRequired(false)
      .OnDelete(DeleteBehavior.Restrict);

    builder.Property(a => a.Source)
      .HasColumnName("source")
      .HasConversion<int>()
      .HasDefaultValue(AppointmentSource.Panel)
      .IsRequired();

    builder.Property(a => a.SelfServiceChangedAt)
      .HasColumnName("self_service_changed_at")
      .IsRequired(false);

    builder.Property(a => a.SelfServiceChangeKind)
      .HasColumnName("self_service_change_kind")
      .IsRequired(false);

    builder.HasIndex(a => a.SelfServiceChangedAt)
      .HasFilter("self_service_changed_at IS NOT NULL");

    builder.Property(a => a.AnonSessionId)
      .HasColumnName("anon_session_id")
      .IsRequired(false);

    builder.Property(a => a.Reminder24hSentAtUtc)
      .HasColumnName("reminder_24h_sent_at_utc")
      .IsRequired(false);

    builder.Property(a => a.Reminder2hSentAtUtc)
      .HasColumnName("reminder_2h_sent_at_utc")
      .IsRequired(false);

    // Zadatek — nullable owned Money (obie kolumny null gdy zadatek nigdy nie wymagany).
    builder.OwnsOne(a => a.DepositAmount, dep =>
    {
      dep.Property(p => p.Amount).HasColumnName("deposit_amount").HasPrecision(18, 2);
      dep.Property(p => p.Currency).HasColumnName("deposit_currency").HasMaxLength(3);
    });

    builder.Property(a => a.PaymentStatus)
      .HasColumnName("payment_status")
      .HasConversion<int>()
      .HasDefaultValue(AppointmentPaymentStatus.NotRequired)
      .IsRequired();

    builder.Property(a => a.PaymentSessionId)
      .HasColumnName("payment_session_id")
      .HasMaxLength(255)
      .IsRequired(false);

    builder.Property(a => a.PaymentLinkUrl)
      .HasColumnName("payment_link_url")
      .HasMaxLength(1000)
      .IsRequired(false);

    builder.Property(a => a.PaidAtUtc)
      .HasColumnName("paid_at_utc")
      .IsRequired(false);

    builder.Property(a => a.LinkExpiresAtUtc)
      .HasColumnName("link_expires_at_utc")
      .IsRequired(false);

    builder.Property(a => a.DepositLinkSentAtUtc)
      .HasColumnName("deposit_link_sent_at_utc")
      .IsRequired(false);

    builder.Property(a => a.DepositLinkSentChannel)
      .HasColumnName("deposit_link_sent_channel")
      .HasMaxLength(16)
      .IsRequired(false);

    builder.Property(a => a.DepositLinkAttempts)
      .HasColumnName("deposit_link_attempts")
      .HasDefaultValue(0);

    builder.Property(a => a.ExpiredDepositLinkCount)
      .HasColumnName("expired_deposit_link_count")
      .HasDefaultValue(0);

    // Szybkie filtrowanie niezapłaconych zadatków (UI / przyszły cleanup). 1 = AwaitingPayment.
    builder.HasIndex(a => new { a.PaymentStatus, a.LinkExpiresAtUtc })
      .HasFilter("payment_status = 1");

    // Lookup po anon-session — szybka detekcja "poprzednich holdów tej sesji" przy każdym nowym /hold.
    builder.HasIndex(a => a.AnonSessionId)
      .HasFilter("anon_session_id IS NOT NULL");

    builder.HasIndex(a => new { a.TenantId, a.Date, a.EmployeeId });

    // Panel self-service klienta (GetSelfServiceAppointmentsQuery) filtruje po (TenantId, CustomerId,
    // Date>=) — anonimowa, gorąca ścieżka. Bez tego indeksu zapytanie szło po (TenantId, Date, EmployeeId)
    // i filtrowało CustomerId resztkowo, degradując latencję wraz ze wzrostem tabeli Appointments.
    builder.HasIndex(a => new { a.TenantId, a.CustomerId, a.Date });

    // Job cyklu życia skanuje wizyty po (Status, zakres Date) co 1 min — indeks zamienia Seq Scan
    // na range scan i utrzymuje koszt stały niezależnie od rozmiaru tabeli (zob. AppointmentStatusLifecycleHostedService).
    builder.HasIndex(a => new { a.Status, a.Date });

    // Wyklucz anulowane: HasCollisionAsync ignoruje Canceled, więc pełny UNIQUE bez filtra
    // powodował DbUpdateException przy ponownej rezerwacji tego samego slotu.
    builder.HasIndex(a => new { a.EmployeeId, a.Date, a.StartTime })
      .IsUnique()
      .HasFilter("\"Status\" <> 'Canceled'");

    // xmin: systemowa kolumna PostgreSQL (nie wymaga migracji). Gwarantuje, że SaveChanges
    // rzuci DbUpdateConcurrencyException, gdy wiersz został usunięty lub zmieniony przez
    // inny proces (np. background job) między załadowaniem a zapisem.
    builder.Property<uint>("xmin")
      .HasColumnType("xid")
      .ValueGeneratedOnAddOrUpdate()
      .IsConcurrencyToken();
  }
}