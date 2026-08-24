using App.Domain.Aggregates.EmployeeAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class ShiftTemplateConfiguration : IEntityTypeConfiguration<ShiftTemplate>
{
  public void Configure(EntityTypeBuilder<ShiftTemplate> builder)
  {
    builder.ToTable("ShiftTemplates");

    builder.HasKey(x => x.Id);
    builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

    builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
    builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
    builder.Property(x => x.SlotGenerationMode)
      .HasColumnName("slot_generation_mode")
      .HasConversion<int>()
      .HasDefaultValue(SlotGenerationMode.Grid)
      .IsRequired();

    builder.OwnsOne(x => x.ScheduleDay, day =>
    {
      day.Property(x => x.CycleIndex).HasColumnName("cycle_index");

      // Tryb stałych godzin — lista godzin startu jako kolumna tablicy (analogicznie do ScheduleDay grafiku).
      day.PrimitiveCollection(x => x.FixedStartTimes).HasColumnName("fixed_start_times");

      day.OwnsMany(x => x.WorkRanges, wr =>
      {
        wr.ToTable("ShiftTemplateWorkRanges");
        wr.Property<Guid>("shift_template_id");
        wr.WithOwner().HasForeignKey("shift_template_id");
        wr.Property<Guid>("Id").HasColumnName("id");
        wr.HasKey("Id");
        wr.Property(p => p.StartTime).HasColumnName("start_time").IsRequired();
        wr.Property(p => p.EndTime).HasColumnName("end_time").IsRequired();
      });

      day.OwnsMany(x => x.Breaks, b =>
      {
        b.ToTable("ShiftTemplateBreaks");
        b.Property<Guid>("shift_template_id");
        b.WithOwner().HasForeignKey("shift_template_id");
        b.Property<Guid>("Id").HasColumnName("id");
        b.HasKey("Id");
        b.Property(x => x.StartTime).HasColumnName("start_time").IsRequired();
        b.Property(x => x.EndTime).HasColumnName("end_time").IsRequired();
      });
    });
  }
}
