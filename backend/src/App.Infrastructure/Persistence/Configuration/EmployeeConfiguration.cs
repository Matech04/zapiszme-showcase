using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
  public void Configure(EntityTypeBuilder<Employee> builder)
  {
    builder.ToTable("Employees");

    builder.HasKey(e => e.Id);

    builder.Property(e => e.UserId)
      .HasColumnName("user_id")
      .IsRequired(false);

    builder.HasOne<User>()
      .WithMany()
      .HasForeignKey(e => e.UserId)
      .OnDelete(DeleteBehavior.SetNull);

    builder.HasIndex(e => e.UserId)
      .IsUnique()
      .HasFilter("user_id IS NOT NULL");

    builder.Property(e => e.FirstName)
      .HasColumnName("first_name")
      .HasMaxLength(100)
      .IsRequired();

    builder.Property(e => e.LastName)
      .HasColumnName("last_name")
      .HasMaxLength(100)
      .IsRequired();

    builder.Property(e => e.Email)
      .HasColumnName("email")
      .HasMaxLength(100)
      .IsRequired();

    builder.Property(e => e.Specialization)
      .HasColumnName("specialization")
      .HasMaxLength(100)
      .IsRequired(false);

    builder.Property(e => e.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true);

    builder.Property(e => e.IsBookable)
      .HasColumnName("is_bookable")
      .HasDefaultValue(true);

    builder.Property(e => e.SlotGenerationMode)
      .HasColumnName("slot_generation_mode")
      .HasConversion<int>()
      .HasDefaultValue(SlotGenerationMode.Grid)
      .IsRequired();

    // „Papierowy kalendarz" — deklaracja, że pracownik nie prowadzi grafiku powtarzalnego.
    // Default false = dotychczasowe zachowanie dla wszystkich istniejących pracowników.
    builder.Property(e => e.UsesAdHocSchedule)
      .HasColumnName("uses_ad_hoc_schedule")
      .HasDefaultValue(false)
      .IsRequired();

    builder.Property(e => e.TenantId)
      .HasColumnName("tenant_id")
      .IsRequired();

    builder.HasOne<Tenant>()
      .WithMany()
      .HasForeignKey(e => e.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    builder.OwnsMany(e => e.Services, s =>
    {
      s.ToTable("EmployeeServices");

      // Klucz ustawiany w konstruktorze EmployeeService (Guid.NewGuid()).
      // ValueGeneratedOnAdd() powodowało, że EF traktował Id jak generowane przez DB i przy zapisie
      // dochodziło do UPDATE zamiast INSERT / drugiego UPDATE z 0 wierszy → DbUpdateConcurrencyException.
      s.Property(x => x.Id).ValueGeneratedNever();
      s.HasKey(x => x.Id);

      s.WithOwner().HasForeignKey(x => x.EmployeeId);

      // 2. TenantId jako jawna kolumna i część relacji
      s.Property(x => x.TenantId)
          .HasColumnName("tenant_id")
          .IsRequired();

      // 3. Reszta pól
      s.Property(x => x.ServiceId)
          .HasColumnName("service_id")
          .IsRequired();

      // null = dziedzicz czas z katalogu usługi (Service.DurationInMinutes); wartość = override per-pracownik.
      s.Property(x => x.CustomDuration)
          .HasColumnName("custom_duration")
          .IsRequired(false);

      s.OwnsOne(x => x.CustomPrice, price =>
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

      // 4. Klucz unikalny (zamiast starego HasKey)
      // Zapobiega przypisaniu tej samej usługi temu samemu pracownikowi dwa razy
      s.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ServiceId }).IsUnique();

      // Relacje
      s.HasOne<Service>()
          .WithMany()
          .HasForeignKey(s => s.ServiceId)
          .OnDelete(DeleteBehavior.Restrict);

      s.HasOne<Tenant>()
          .WithMany()
          .HasForeignKey(s => s.TenantId)
          .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Navigation(e => e.Services)
      .HasField("_services")
      .UsePropertyAccessMode(PropertyAccessMode.Field);

    builder.OwnsMany(e => e.Schedules, s =>
    {
      s.ToTable("EmployeeSchedules");
      s.WithOwner().HasForeignKey("employee_id");
      s.HasKey(x => x.Id);

      s.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
      s.Property(x => x.NumberOfCycles).HasColumnName("number_of_cycles").IsRequired();
      s.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);

      s.OwnsOne(x => x.ActiveRange, ar =>
      {
        ar.Property(p => p.StartDate).HasColumnName("active_from").IsRequired();
        ar.Property(p => p.EndDate).HasColumnName("active_to").IsRequired();
      });
      s.Navigation(x => x.ActiveRange).IsRequired();

      s.OwnsMany(x => x.ScheduleDays, d =>
      {
        d.ToTable("ScheduleDays");
        d.WithOwner().HasForeignKey("employee_schedule_id");
        d.HasKey(x => x.Id);

        d.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        d.Property(x => x.CycleIndex).HasColumnName("cycle_index").IsRequired();

        d.OwnsMany(x => x.WorkRanges, wr =>
        {
          wr.ToTable("ScheduleDayWorkRanges");
          wr.WithOwner().HasForeignKey("schedule_day_id");
          // Id jest shadow property — EF wygeneruje nowe Guid przy Add (bez ValueGeneratedNever).
          wr.Property<Guid>("Id").HasColumnName("id");
          wr.HasKey("Id");
          wr.Property(p => p.StartTime).HasColumnName("start_time").IsRequired();
          wr.Property(p => p.EndTime).HasColumnName("end_time").IsRequired();
        });

        d.OwnsMany(x => x.Breaks, b =>
        {
          b.ToTable("ScheduleDayBreaks");
          b.WithOwner().HasForeignKey("schedule_day_id");
          b.Property<Guid>("Id").HasColumnName("id");
          b.HasKey("Id");
          b.Property(x => x.StartTime).HasColumnName("start_time").IsRequired();
          b.Property(x => x.EndTime).HasColumnName("end_time").IsRequired();
        });

        // Tryb stałych slotów — lista godzin startu jako kolumna jsonb (zamiast osobnej tabeli).
        d.PrimitiveCollection(x => x.FixedStartTimes).HasColumnName("fixed_start_times");
      });
    });

    builder.OwnsMany(e => e.Overrides, o =>
    {
      o.ToTable("ScheduleOverrides");
      o.WithOwner().HasForeignKey("employee_id");
      o.HasKey(x => x.Id);
      o.Ignore(x => x.WorkRanges);
      o.Ignore(x => x.Breaks);

      o.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
      o.Property(x => x.Date).HasColumnName("date").IsRequired();

      o.OwnsOne(x => x.ScheduleDay, d =>
      {
        d.Property(x => x.CycleIndex).HasColumnName("cycle_index");

        d.OwnsMany(x => x.WorkRanges, wr =>
        {
          wr.ToTable("ScheduleOverrideWorkRanges");
          wr.WithOwner().HasForeignKey("schedule_override_id");
          wr.Property<Guid>("Id").HasColumnName("id");
          wr.HasKey("Id");
          wr.Property(p => p.StartTime).HasColumnName("start_time").IsRequired();
          wr.Property(p => p.EndTime).HasColumnName("end_time").IsRequired();
        });

        d.OwnsMany(x => x.Breaks, b =>
        {
          b.ToTable("ScheduleOverrideBreaks");
          b.WithOwner().HasForeignKey("schedule_override_id");
          b.Property<Guid>("Id").HasColumnName("id");
          b.HasKey("Id");
          b.Property(x => x.StartTime).HasColumnName("start_time").IsRequired();
          b.Property(x => x.EndTime).HasColumnName("end_time").IsRequired();
        });

        // Tryb stałych slotów dla override'a dnia — analogicznie do dnia bazowego (kolumna jsonb).
        d.PrimitiveCollection(x => x.FixedStartTimes).HasColumnName("fixed_start_times");
      });
    });

    builder.OwnsMany(e => e.Leaves, l =>
    {
      l.ToTable("EmployeeLeaves");
      l.WithOwner().HasForeignKey("EmployeeId");
      l.HasKey(x => x.Id);
      l.Property(x => x.Id).HasColumnName("Id").ValueGeneratedNever();
      // Enumy przechowywane jako int — bez tego mapowania pola wpadały do `Ignore`,
      // a frontend dostawał zawsze wartości domyślne (0), co psuło filtr „Approved + Vacation".
      l.Property(x => x.AbsenceType).HasColumnName("AbsenceType").HasConversion<int>().IsRequired();
      l.Property(x => x.AbsenceStatus).HasColumnName("AbsenceStatus").HasConversion<int>().IsRequired();

      l.OwnsOne(x => x.DateRange, dr =>
      {
        dr.Property(p => p.StartDate).HasColumnName("StartDate").IsRequired();
        dr.Property(p => p.EndDate).HasColumnName("EndDate").IsRequired();
      });
    });

    builder.OwnsMany(e => e.MonthPublications, p =>
    {
      p.ToTable("EmployeeMonthPublications");
      p.WithOwner().HasForeignKey("employee_id");
      p.HasKey(x => x.Id);

      p.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
      p.Property(x => x.Year).HasColumnName("year").IsRequired();
      p.Property(x => x.Month).HasColumnName("month").IsRequired();
      // null = zamknięty bezterminowo (do ręcznego otwarcia); data = otwiera się sam tego dnia.
      p.Property(x => x.OpensOn).HasColumnName("opens_on").IsRequired(false);

      // Jeden wiersz na (pracownik, miesiąc) — agregat i tak tego pilnuje, ale bez indeksu
      // równoległy zapis mógłby zostawić duplikaty, a wtedy FindMonthPublication wybiera losowo.
      p.HasIndex("employee_id", nameof(MonthPublication.Year), nameof(MonthPublication.Month)).IsUnique();
    });

    builder.Navigation(e => e.Services).HasField("_services").UsePropertyAccessMode(PropertyAccessMode.Field);
    builder.Navigation(e => e.Schedules).HasField("_schedules").UsePropertyAccessMode(PropertyAccessMode.Field);
    builder.Navigation(e => e.Overrides).HasField("_overrides").UsePropertyAccessMode(PropertyAccessMode.Field);
    builder.Navigation(e => e.Leaves).HasField("_leaves").UsePropertyAccessMode(PropertyAccessMode.Field);
    builder.Navigation(e => e.MonthPublications).HasField("_monthPublications").UsePropertyAccessMode(PropertyAccessMode.Field);
  }
}
