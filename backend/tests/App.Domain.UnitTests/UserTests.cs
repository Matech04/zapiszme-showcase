using App.Domain.Aggregates.UserAggregate;

namespace App.Domain.UnitTests;

public class UserTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var email = "test@example.com";
    var displayName = "Test User";

    // Act
    var user = new User(email, displayName);

    // Assert
    Assert.NotEqual(Guid.Empty, user.Id);
    Assert.Equal(email, user.Email);
    Assert.Equal(email, user.UserName);
    Assert.Equal(displayName, user.DisplayName);
  }

  [Fact]
  public void UpdateProfile_ShouldUpdateValuesCorrectly()
  {
    // Arrange
    var user = new User("old@example.com", "Old Name");
    var newEmail = "new@example.com";
    var newDisplayName = "New Name";

    // Act
    user.UpdateProfile(newEmail, newDisplayName);

    // Assert
    Assert.Equal(newEmail, user.Email);
    Assert.Equal(newEmail, user.UserName);
    Assert.Equal(newDisplayName, user.DisplayName);
  }

  [Theory]
  [InlineData("", "Display Name")]
  [InlineData("email", "")]
  [InlineData(null, "Display Name")]
  [InlineData("email", null)]
  public void UpdateProfile_WithInvalidInput_ShouldThrowException(string? email, string? displayName)
  {
    // Arrange
    var user = new User("test@example.com", "Test User");

    // Act & Assert
    Assert.ThrowsAny<Exception>(() => user.UpdateProfile(email!, displayName!));
  }

  [Fact]
  public void New_user_has_no_pending_promo_code()
  {
    var user = new User("test@example.com", "Test User");

    Assert.Null(user.PendingPromoCode);
  }

  [Fact]
  public void SetPendingPromoCode_normalizes_trim_and_upper()
  {
    var user = new User("test@example.com", "Test User");

    user.SetPendingPromoCode("  founding10 ");

    Assert.Equal("FOUNDING10", user.PendingPromoCode);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void SetPendingPromoCode_blank_becomes_null(string? code)
  {
    var user = new User("test@example.com", "Test User");

    user.SetPendingPromoCode(code);

    Assert.Null(user.PendingPromoCode);
  }

  [Fact]
  public void ClearPendingPromoCode_removes_stored_code()
  {
    var user = new User("test@example.com", "Test User");
    user.SetPendingPromoCode("PROMO");

    user.ClearPendingPromoCode();

    Assert.Null(user.PendingPromoCode);
  }
}