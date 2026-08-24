using App.Application.Booking.PromoCodes.Queries.ValidatePromoCode;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking.PromoCodes;

public sealed class ValidatePromoCodeQueryTests
{
  [Fact]
  public async Task Validate_returns_invalid_for_nonexistent_code()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();

    var handler = new ValidatePromoCodeQueryHandler(db);
    var result = await handler.Handle(new ValidatePromoCodeQuery("NONEXISTENT"), ct);

    Assert.False(result.IsValid);
  }

  [Fact]
  public async Task Validate_returns_valid_with_preview_for_active_PriceOverride()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();

    var code = PromoCode.CreatePriceOverride("FOUNDING10", 49m, maxTotalUses: 10);
    db.PromoCodes.Add(code);
    db.SaveChanges();

    var handler = new ValidatePromoCodeQueryHandler(db);
    var result = await handler.Handle(new ValidatePromoCodeQuery("founding10"), ct);

    Assert.True(result.IsValid);
    Assert.NotNull(result.DiscountPreview);
    Assert.Contains("49", result.DiscountPreview);
  }

  [Fact]
  public async Task Validate_returns_invalid_for_expired_code_without_revealing_reason()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();

    var code = PromoCode.CreatePriceOverride(
      "EXPIRED", 49m,
      validFrom: DateTime.UtcNow.AddDays(-30),
      validUntil: DateTime.UtcNow.AddDays(-1));
    db.PromoCodes.Add(code);
    db.SaveChanges();

    var handler = new ValidatePromoCodeQueryHandler(db);
    var result = await handler.Handle(new ValidatePromoCodeQuery("EXPIRED"), ct);

    Assert.False(result.IsValid);
    // Generic message — nie ujawnia, że kod konkretnie wygasł vs nie istnieje.
    Assert.Equal("Kod nieprawidłowy.", result.Message);
  }

  [Fact]
  public async Task Validate_returns_invalid_when_MaxTotalUses_reached()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();

    var code = PromoCode.CreatePriceOverride("CAPPED", 49m, maxTotalUses: 1);
    code.IncrementUses();
    db.PromoCodes.Add(code);
    db.SaveChanges();

    var handler = new ValidatePromoCodeQueryHandler(db);
    var result = await handler.Handle(new ValidatePromoCodeQuery("CAPPED"), ct);

    Assert.False(result.IsValid);
  }

  private static ApplicationDbContext SetupDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService());
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
