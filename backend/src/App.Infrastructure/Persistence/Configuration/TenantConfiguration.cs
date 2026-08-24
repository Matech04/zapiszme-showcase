using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
  public void Configure(EntityTypeBuilder<Tenant> builder)
  {
    builder.ToTable("Tenants");

    builder.HasKey(t => t.Id);
    builder.Property(t => t.Name).HasColumnName("name").IsRequired();
    builder.Property(t => t.Slug).HasColumnName("slug").IsRequired();
    // Slug = publiczny URL salonu, rozwiązywany przy KAŻDYM żądaniu bookingu ({slug}→tenant w
    // TenantIdentifierMiddleware / GetPublicBookingSalon). Unikalny indeks wymusza unikalność ORAZ
    // eliminuje seq-scan na najgorętszej, anonimowej ścieżce.
    builder.HasIndex(t => t.Slug).IsUnique();

    // White-label: bazowa domena klienta (np. "salon-przyklad.pl"). Nullable — większość tenantów
    // jej nie ma. Hosty rezerwacja./api. wyprowadzane konwencją w resolve-host / tls-allowed.
    builder.Property(t => t.CustomDomain)
      .HasColumnName("custom_domain")
      .HasMaxLength(253); // max długość nazwy domeny (RFC 1035)
    builder.Property(t => t.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired().HasDefaultValue("Europe/Warsaw");
    builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired().HasDefaultValue("PLN");
    builder.Property(t => t.Industry).HasColumnName("industry").HasMaxLength(64);
    builder.Property(t => t.OnboardingCompletedAt).HasColumnName("onboarding_completed_at");
    builder.Property(t => t.CustomerVerificationChannel)
      .HasColumnName("customer_verification_channel")
      .IsRequired();

    builder.Property(t => t.AppointmentSlotStepMinutes)
      .HasColumnName("appointment_slot_step_minutes")
      .IsRequired()
      .HasDefaultValue(15);

    // 120 dni — dawne MAX_MONTHS_AHEAD = 3 sięgało do KOŃCA miesiąca „bieżący + 3", czyli 92–123 dni
    // zależnie od dnia miesiąca. 90 zawęziłoby okno istniejącym salonom; 120 jest neutralne.
    builder.Property(t => t.BookingHorizonDays)
      .HasColumnName("booking_horizon_days")
      .IsRequired()
      .HasDefaultValue(120);

    builder.Property(t => t.RequireCustomerName)
      .HasColumnName("require_customer_name")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(t => t.CollectInstagramHandle)
      .HasColumnName("collect_instagram_handle")
      .IsRequired()
      .HasDefaultValue(false);

    // Domyślnie false — funkcja inspiracji jest opt-in (salon świadomie ją włącza w ustawieniach).
    builder.Property(t => t.CollectInspirationImages)
      .HasColumnName("collect_inspiration_images")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(t => t.BookingCalendarColorHex)
      .HasColumnName("booking_calendar_color_hex")
      .HasMaxLength(7)
      .IsRequired(false);

    builder.Property(t => t.BookingCalendarBackgroundHex)
      .HasColumnName("booking_calendar_background_hex")
      .HasMaxLength(7)
      .IsRequired(false);

    builder.Property(t => t.BookingCalendarSurfaceHex)
      .HasColumnName("booking_calendar_surface_hex")
      .HasMaxLength(7)
      .IsRequired(false);

    builder.Property(t => t.BookingCalendarPriceHex)
      .HasColumnName("booking_calendar_price_hex")
      .HasMaxLength(7)
      .IsRequired(false);

    // Regulamin salonu — długi tekst (plain), nullable. Bez indeksu (niewyszukiwalny).
    builder.Property(t => t.TermsOfService)
      .HasColumnName("terms_of_service")
      .HasColumnType("text")
      .IsRequired(false);

    // Tryb „nie przechowuj historii wizyt" — job hard-kasuje terminalne/przeszłe wizyty tego salonu.
    builder.Property(t => t.DoNotRetainAppointmentHistory)
      .HasColumnName("do_not_retain_appointment_history")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(t => t.UncategorizedOrderIndex)
      .HasColumnName("uncategorized_order_index")
      .IsRequired()
      .HasDefaultValue(Tenant.UncategorizedOrderDefault);

    builder.Property(t => t.BookingAccessPolicy)
      .HasColumnName("booking_access_policy")
      .HasConversion<int>()
      .IsRequired()
      .HasDefaultValue(BookingAccessPolicy.Open);

    builder.Property(t => t.AppointmentConfirmationMode)
      .HasColumnName("appointment_confirmation_mode")
      .HasConversion<int>()
      .IsRequired()
      .HasDefaultValue(AppointmentConfirmationMode.Automatic);

    builder.Property(t => t.StaffCalendarVisibilityPolicy)
      .HasColumnName("staff_calendar_visibility_policy")
      .HasConversion<int>()
      .IsRequired()
      .HasDefaultValue(StaffCalendarVisibilityPolicy.OwnCalendarOnly);

    builder.Property(t => t.IsDemo)
      .HasColumnName("is_demo")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(t => t.DemoCreatedAtUtc)
      .HasColumnName("demo_created_at_utc")
      .IsRequired(false);

    builder.Property(t => t.BookingPaused)
      .HasColumnName("booking_pause_enabled")
      .IsRequired()
      .HasDefaultValue(false);

    builder.Property(t => t.BookingPauseMessage)
      .HasColumnName("booking_pause_message")
      .HasMaxLength(Tenant.BookingPauseMessageMaxLength)
      .IsRequired(false);

    // Indeks pod cykliczny cleanup demo-tenantów (DemoTenantCleanupHostedService).
    builder.HasIndex(t => new { t.IsDemo, t.DemoCreatedAtUtc })
      .HasDatabaseName("ix_tenants_is_demo_demo_created_at_utc");

    // Unikalny per domena, ale tylko dla ustawionych (partial index) — wiele NULL-i dozwolone.
    // Chroni przed przypisaniem tej samej customowej domeny do dwóch tenantów (host→tenant musi być 1:1).
    builder.HasIndex(t => t.CustomDomain)
      .HasDatabaseName("ix_tenants_custom_domain")
      .IsUnique()
      .HasFilter("custom_domain IS NOT NULL");

    builder.OwnsOne(t => t.GapFillingSettings, gfs =>
    {
      gfs.Property(g => g.Mode)
        .HasColumnName("gap_filling_mode")
        .HasConversion<int>();
      gfs.Property(g => g.BufferMinutes)
        .HasColumnName("gap_filling_buffer_minutes");
      gfs.Property(g => g.LookaheadSlots)
        .HasColumnName("gap_filling_lookahead_slots");
    });

    builder.OwnsOne(t => t.NotificationSettings, ns =>
    {
      ns.Property(n => n.NewBookingToSalon)
        .HasColumnName("notify_new_booking_to_salon").HasDefaultValue(true);
      ns.Property(n => n.BookingConfirmationToCustomer)
        .HasColumnName("notify_booking_confirmation_to_customer").HasDefaultValue(false);
      ns.Property(n => n.CancellationToSalon)
        .HasColumnName("notify_cancellation_to_salon").HasDefaultValue(true);
      ns.Property(n => n.CancellationToCustomer)
        .HasColumnName("notify_cancellation_to_customer").HasDefaultValue(true);
      ns.Property(n => n.RescheduleToSalon)
        .HasColumnName("notify_reschedule_to_salon").HasDefaultValue(true);
      ns.Property(n => n.RescheduleToCustomer)
        .HasColumnName("notify_reschedule_to_customer").HasDefaultValue(true);
      ns.Property(n => n.AppointmentReminderToCustomer)
        .HasColumnName("notify_appointment_reminder_to_customer").HasDefaultValue(true);
      ns.Property(n => n.AwaitingConfirmationToSalon)
        .HasColumnName("notify_awaiting_confirmation_to_salon").HasDefaultValue(true);
      ns.Property(n => n.CancelledBySalonToCustomer)
        .HasColumnName("notify_cancelled_by_salon_to_customer").HasDefaultValue(true);
      ns.Property(n => n.RescheduledBySalonToCustomer)
        .HasColumnName("notify_rescheduled_by_salon_to_customer").HasDefaultValue(true);
      ns.Property(n => n.AppointmentReminder2hToCustomer)
        .HasColumnName("notify_appointment_reminder_2h_to_customer").HasDefaultValue(false);
      ns.Property(n => n.StaffBookedAppointmentToCustomer)
        .HasColumnName("notify_staff_booked_appointment_to_customer").HasDefaultValue(true);
    });
    builder.Navigation(t => t.NotificationSettings).IsRequired();

    builder.OwnsOne(t => t.Subscription, sub =>
    {
      sub.Property(s => s.Status)
        .HasColumnName("subscription_status")
        .HasConversion<int>()
        .IsRequired();

      sub.Property(s => s.Seats)
        .HasColumnName("subscription_seats")
        .HasDefaultValue(1)
        .IsRequired();

      sub.Property(s => s.IsFoundingMember)
        .HasColumnName("subscription_is_founding_member")
        .HasDefaultValue(false)
        .IsRequired();

      sub.Property(s => s.TrialEndsAt)
        .HasColumnName("subscription_trial_ends_at")
        .IsRequired(false);

      sub.Property(s => s.CurrentPeriodEndsAt)
        .HasColumnName("subscription_current_period_ends_at")
        .IsRequired(false);

      sub.Property(s => s.ActivePromoCodeRedemptionId)
        .HasColumnName("subscription_active_promo_code_redemption_id")
        .IsRequired(false);

      sub.Property(s => s.MonthlySmsHardCap)
        .HasColumnName("subscription_monthly_sms_hard_cap")
        .IsRequired(false);

      sub.Ignore(s => s.EffectiveStatus);
      sub.Ignore(s => s.IsTrialActive);
      sub.Ignore(s => s.DaysRemainingInTrial);
      sub.Ignore(s => s.MonthlyPriceInGrosze);
      sub.Ignore(s => s.MonthlySmsAllowance);
      sub.Ignore(s => s.EffectiveMonthlySmsCap);
    });

    builder.OwnsOne(t => t.DepositSettings, ds =>
    {
      ds.Property(d => d.Enabled)
        .HasColumnName("deposit_enabled")
        .HasDefaultValue(false)
        .IsRequired();
      ds.Property(d => d.Mode)
        .HasColumnName("deposit_mode")
        .HasConversion<int>()
        .HasDefaultValue(DepositMode.Percentage)
        .IsRequired();
      ds.Property(d => d.Value)
        .HasColumnName("deposit_value")
        .HasPrecision(18, 2)
        .HasDefaultValue(0m)
        .IsRequired();
      ds.Property(d => d.Instrument)
        .HasColumnName("deposit_instrument")
        .HasConversion<int>()
        .HasDefaultValue(DepositInstrument.Zadatek)
        .IsRequired();
    });
    builder.Navigation(t => t.DepositSettings).IsRequired();

    // Nullable owned — wszystkie kolumny null dopóki salon nie połączy konta płatności.
    builder.OwnsOne(t => t.MerchantAccount, ma =>
    {
      ma.Property(m => m.Provider)
        .HasColumnName("merchant_provider")
        .HasMaxLength(50);
      ma.Property(m => m.AccountId)
        .HasColumnName("merchant_account_id")
        .HasMaxLength(255);
      ma.Property(m => m.OnboardingStatus)
        .HasColumnName("merchant_onboarding_status")
        .HasConversion<int>();
      ma.Property(m => m.ChargesEnabled)
        .HasColumnName("merchant_charges_enabled");
    });
  }
}