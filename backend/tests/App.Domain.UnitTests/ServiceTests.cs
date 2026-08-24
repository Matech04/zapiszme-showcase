using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;

namespace App.Domain.UnitTests;

public class ServiceTests
{
  [Fact]
  public void ServiceConstructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var categoryId = Guid.NewGuid();
    var vatRateId = Guid.NewGuid();
    var name = "Haircut";
    var price = new Money(50.00m, "PLN");
    var duration = 30;

    // Act
    var service = new Service(tenantId, categoryId, vatRateId, name, price, duration);

    // Assert
    Assert.NotEqual(Guid.Empty, service.Id);
    Assert.Equal(tenantId, service.TenantId);
    Assert.Equal(categoryId, service.CategoryId);
    Assert.Equal(vatRateId, service.VatRateId);
    Assert.Equal(name, service.Name);
    Assert.Equal(price, service.Price);
    Assert.Equal(duration, service.DurationInMinutes);
  }

  [Fact]
  public void ServiceUpdate_ShouldUpdateValuesCorrectly()
  {
    // Arrange
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Old", new Money(10m, "PLN"), 10);
    var newCategoryId = Guid.NewGuid();
    var newVatRateId = Guid.NewGuid();
    var newName = "New";
    var newPrice = new Money(20m, "PLN");
    var newDuration = 20;

    // Act
    service.Update(newCategoryId, newVatRateId, newName, newPrice, newDuration);

    // Assert
    Assert.Equal(newCategoryId, service.CategoryId);
    Assert.Equal(newVatRateId, service.VatRateId);
    Assert.Equal(newName, service.Name);
    Assert.Equal(newPrice, service.Price);
    Assert.Equal(newDuration, service.DurationInMinutes);
  }

  [Fact]
  public void CategoryConstructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var name = "Category";
    var order = 1;

    // Act
    var category = new ServiceCategory(tenantId, name, order);

    // Assert
    Assert.NotEqual(Guid.Empty, category.Id);
    Assert.Equal(tenantId, category.TenantId);
    Assert.Equal(name, category.Name);
    Assert.Equal(order, category.OrderIndex);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(-100)]
  public void ServiceUpdate_WithNegativePrice_ShouldThrowException(decimal priceAmount)
  {
    // Arrange
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);

    // Act & Assert
    Assert.ThrowsAny<Exception>(() => service.Update(Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(priceAmount, "PLN"), 10));
  }

  // DOM-SVC-005: SVC-001 Negative — duration ujemny
  [Theory]
  [InlineData(-1)]
  [InlineData(-60)]
  public void ServiceUpdate_WithNegativeDuration_ShouldThrowException(int duration)
  {
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);

    Assert.ThrowsAny<Exception>(() =>
      service.Update(Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(10m, "PLN"), duration));
  }

  // DOM-SVC-006: SVC-001 Negative — name empty/whitespace
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ServiceUpdate_WithEmptyOrWhitespaceName_ShouldThrowException(string name)
  {
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);

    Assert.ThrowsAny<Exception>(() =>
      service.Update(Guid.NewGuid(), Guid.NewGuid(), name, new Money(10m, "PLN"), 10));
  }

  // DOM-SVC-011: Service bez kategorii (Opcja A — CategoryId nullable)
  [Fact]
  public void Service_CanBeCreatedWithoutCategory()
  {
    var service = new Service(Guid.NewGuid(), categoryId: null, Guid.NewGuid(), "Orphan", new Money(50m, "PLN"), 30);

    Assert.Null(service.CategoryId);
    Assert.Equal("Orphan", service.Name);
  }

  [Fact]
  public void ServiceUpdate_CanClearCategory()
  {
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Withcat", new Money(50m, "PLN"), 30);
    Assert.NotNull(service.CategoryId);

    service.Update(categoryId: null, Guid.NewGuid(), "Nowcat", new Money(50m, "PLN"), 30);

    Assert.Null(service.CategoryId);
  }

  // DOM-SVC-009: SVC-003 Service.Deactivate
  [Fact]
  public void ServiceDeactivate_SetsIsActiveFalse()
  {
    var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);
    Assert.True(service.IsActive);

    service.Deactivate();

    Assert.False(service.IsActive);
  }

  // DOM-SVC-007: SVC-002 ServiceCategory.Update
  [Fact]
  public void ServiceCategoryUpdate_ChangesNameAndOrderIndex()
  {
    var category = new ServiceCategory(Guid.NewGuid(), "Old", 1);

    category.Update("New", 5);

    Assert.Equal("New", category.Name);
    Assert.Equal(5, category.OrderIndex);
  }

  // DOM-SVC-008: SVC-002 Negative — name empty/whitespace
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ServiceCategoryUpdate_WithEmptyOrWhitespaceName_ShouldThrowException(string name)
  {
    var category = new ServiceCategory(Guid.NewGuid(), "Name", 1);

    Assert.ThrowsAny<Exception>(() => category.Update(name, 1));
  }

  // DOM-SVC-010: SVC-004 ServiceCategory.Deactivate
  [Fact]
  public void ServiceCategoryDeactivate_SetsIsActiveFalse()
  {
    var category = new ServiceCategory(Guid.NewGuid(), "Name", 1);

    category.Deactivate();

    Assert.False(category.IsActive);
  }

  // --- Widełki cen + przedział czasu ---

  [Fact]
  public void ServiceUpdate_WithValidPriceRange_SetsPriceMaxAmount()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60);

    service.Update(null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60, priceMaxAmount: 150m);

    Assert.Equal(150m, service.PriceMaxAmount);
  }

  [Fact]
  public void ServiceConstructor_WithoutRanges_LeavesRangeFieldsNull()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60);

    Assert.Null(service.PriceMaxAmount);
    Assert.Null(service.DurationMinMinutes);
    Assert.Null(service.DurationMaxMinutes);
  }

  [Fact]
  public void ServiceUpdate_WithPriceMaxBelowBase_ShouldThrow()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60);

    Assert.ThrowsAny<Exception>(() =>
      service.Update(null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60, priceMaxAmount: 80m));
  }

  [Fact]
  public void ServiceUpdate_WithValidDurationRange_SetsMinAndMax()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 75);

    service.Update(null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 75, durationMinMinutes: 60, durationMaxMinutes: 90);

    Assert.Equal(60, service.DurationMinMinutes);
    Assert.Equal(90, service.DurationMaxMinutes);
    Assert.Equal(75, service.DurationInMinutes); // czas planowany niezależny od przedziału
  }

  [Theory]
  [InlineData(60, null)]
  [InlineData(null, 90)]
  public void ServiceUpdate_WithOnlyOneDurationBound_ShouldThrow(int? min, int? max)
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60);

    Assert.ThrowsAny<Exception>(() =>
      service.Update(null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60, durationMinMinutes: min, durationMaxMinutes: max));
  }

  [Fact]
  public void ServiceUpdate_WithDurationMinAboveMax_ShouldThrow()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60);

    Assert.ThrowsAny<Exception>(() =>
      service.Update(null, Guid.NewGuid(), "Name", new Money(100m, "PLN"), 60, durationMinMinutes: 90, durationMaxMinutes: 60));
  }

  // ── Media: opis + galeria zdjęć (#5) ──────────────────────────────────────────────────

  [Fact]
  public void Service_WithDescription_NormalizesEmptyToNull()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10, description: "  ");
    Assert.Null(service.Description);

    service.Update(null, Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10, description: "  Opis  ");
    Assert.Equal("Opis", service.Description);
  }

  [Fact]
  public void SetImages_AssignsSequentialOrderIndex_FirstIsCover()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);

    service.SetImages(new[]
    {
      new ServiceImageData("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
      new ServiceImageData("https://cdn/b.webp", "https://cdn/b-t.webp", "services/b.webp"),
    });

    var ordered = service.Images.OrderBy(i => i.OrderIndex).ToList();
    Assert.Equal(2, ordered.Count);
    Assert.Equal(0, ordered[0].OrderIndex);
    Assert.Equal("services/a.webp", ordered[0].StorageKey);
    Assert.Equal(1, ordered[1].OrderIndex);
  }

  [Fact]
  public void SetImages_AboveCap_Throws()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);
    var tooMany = Enumerable.Range(0, Service.MaxImages + 1)
      .Select(i => new ServiceImageData($"https://cdn/{i}.webp", $"https://cdn/{i}-t.webp", $"services/{i}.webp"))
      .ToList();

    Assert.ThrowsAny<ArgumentException>(() => service.SetImages(tooMany));
  }

  [Fact]
  public void SetImages_ReturnsRemovedStorageKeys_ForReconcile()
  {
    var service = new Service(Guid.NewGuid(), null, Guid.NewGuid(), "Name", new Money(10m, "PLN"), 10);
    service.SetImages(new[]
    {
      new ServiceImageData("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
      new ServiceImageData("https://cdn/b.webp", "https://cdn/b-t.webp", "services/b.webp"),
    });

    // Reconcile: zostaw tylko 'a', dołóż 'c' → 'b' powinien wrócić jako usunięty klucz.
    var removed = service.SetImages(new[]
    {
      new ServiceImageData("https://cdn/a.webp", "https://cdn/a-t.webp", "services/a.webp"),
      new ServiceImageData("https://cdn/c.webp", "https://cdn/c-t.webp", "services/c.webp"),
    });

    Assert.Equal(new[] { "services/b.webp" }, removed);
    Assert.Equal(2, service.Images.Count);
    Assert.DoesNotContain(service.Images, i => i.StorageKey == "services/b.webp");
  }
}