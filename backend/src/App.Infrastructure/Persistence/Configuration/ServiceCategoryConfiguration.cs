using App.Domain.Aggregates.ServiceAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
  public void Configure(EntityTypeBuilder<ServiceCategory> builder)
  {
    builder.ToTable("ServiceCategories");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.TenantId)
      .HasColumnName("tenant_id")
      .IsRequired();

    builder.Property(x => x.Name)
      .HasColumnName("name")
      .HasMaxLength(200)
      .IsRequired();

    builder.Property(x => x.OrderIndex)
      .HasColumnName("order_index")
      .IsRequired();

    builder.Property(x => x.IsActive)
      .HasColumnName("is_active")
      .HasDefaultValue(true);
  }
}
