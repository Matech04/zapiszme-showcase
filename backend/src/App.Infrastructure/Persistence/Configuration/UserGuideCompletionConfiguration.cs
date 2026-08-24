using App.Domain.Aggregates.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configuration;

public class UserGuideCompletionConfiguration : IEntityTypeConfiguration<UserGuideCompletion>
{
  public void Configure(EntityTypeBuilder<UserGuideCompletion> builder)
  {
    builder.ToTable("UserGuideCompletions");

    builder.HasKey(c => c.Id);

    builder.Property(c => c.UserId).IsRequired();
    builder.Property(c => c.GuideId).HasMaxLength(64).IsRequired();
    builder.Property(c => c.CompletedAtUtc).IsRequired();

    // Cascade, a nie Restrict: postęp przewodników bez użytkownika jest bezwartościowy i nie jest
    // historią międzyagregatową (w odróżnieniu od wizyt, które muszą przetrwać deaktywację klienta).
    builder.HasOne<User>()
      .WithMany()
      .HasForeignKey(c => c.UserId)
      .OnDelete(DeleteBehavior.Cascade);

    // Unikalny indeks zamienia „ukończ przewodnik" w operację idempotentną: powtórny zapis
    // odbija się o bazę zamiast tworzyć duplikat. Pokrywa też odczyt listy dla użytkownika.
    builder.HasIndex(c => new { c.UserId, c.GuideId }).IsUnique();
  }
}
