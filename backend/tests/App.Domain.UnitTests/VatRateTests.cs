using App.Domain.Aggregates.VatRateAggregate;

namespace App.Domain.UnitTests;

public class VatRateTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var name = "Standard";
    var value = 0.23m;
    var isDefault = true;

    // Act
    var vatRate = new VatRate(tenantId, name, value, isDefault);

    // Assert
    Assert.NotEqual(Guid.Empty, vatRate.Id);
    Assert.Equal(tenantId, vatRate.TenantId);
    Assert.Equal(name, vatRate.Name);
    Assert.Equal(value, vatRate.Value);
    Assert.Equal(isDefault, vatRate.IsDefault);
    Assert.True(vatRate.IsActive);
  }

  [Fact]
  public void Update_ShouldUpdateValuesCorrectly()
  {
    // Arrange
    var vatRate = new VatRate(Guid.NewGuid(), "Old", 0.1m, false);
    var newName = "New";
    var newValue = 0.08m;
    var newIsDefault = true;

    // Act
    vatRate.Update(newName, newValue, newIsDefault);

    // Assert
    Assert.Equal(newName, vatRate.Name);
    Assert.Equal(newValue, vatRate.Value);
    Assert.Equal(newIsDefault, vatRate.IsDefault);
  }

  [Fact]
  public void Deactivate_ShouldSetIsActiveToFalse()
  {
    // Arrange
    var vatRate = new VatRate(Guid.NewGuid(), "Name", 0.23m);

    // Act
    vatRate.Deactivate();

    // Assert
    Assert.False(vatRate.IsActive);
  }

  [Fact]
  public void SetDefault_ShouldSetIsDefaultToTrue()
  {
    // Arrange
    var vatRate = new VatRate(Guid.NewGuid(), "Name", 0.23m, false);

    // Act
    vatRate.SetDefault();

    // Assert
    Assert.True(vatRate.IsDefault);
  }

  [Theory]
  [InlineData(-0.01)]
  [InlineData(1.01)]
  public void Update_WithInvalidValue_ShouldThrowException(decimal value)
  {
    // Arrange
    var vatRate = new VatRate(Guid.NewGuid(), "Name", 0.23m);

    // Act & Assert
    Assert.ThrowsAny<Exception>(() => vatRate.Update("Name", value, false));
  }

  // VAT-004 EdgeCase: UnDefault sets IsDefault=false
  [Fact]
  public void UnDefault_ShouldSetIsDefaultToFalse()
  {
    var vatRate = new VatRate(Guid.NewGuid(), "Name", 0.23m, isDefault: true);
    Assert.True(vatRate.IsDefault);

    vatRate.UnDefault();

    Assert.False(vatRate.IsDefault);
  }
}