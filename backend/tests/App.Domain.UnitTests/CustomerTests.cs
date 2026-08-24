using App.Domain.Aggregates.CustomerAggregate;

namespace App.Domain.UnitTests;

public class CustomerTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var firstName = "John";
    var lastName = "Doe";
    var email = "john@example.com";
    var phone = new PhoneNumber("+48501234567");
    var notes = "Loyal customer";

    // Act
    var customer = new Customer(tenantId, firstName, lastName, email, phone, notes);

    // Assert
    Assert.NotEqual(Guid.Empty, customer.Id);
    Assert.Equal(tenantId, customer.TenantId);
    Assert.Equal(firstName, customer.FirstName);
    Assert.Equal(lastName, customer.LastName);
    Assert.Equal(email, customer.Email);
    Assert.Equal(phone.Value, customer.PhoneNumber.Value);
    Assert.Equal(notes, customer.GeneralNotes);
    Assert.Equal(DateOnly.FromDateTime(DateTime.Now), customer.CreatedAt);
  }

  [Fact]
  public void PhoneNumberSearch_IsKeptInSyncWithPhoneNumber()
  {
    // PhoneNumberSearch to pochodna kolumna wyszukiwarki — same cyfry z E164, utrzymywana
    // automatycznie. Bez tej spójności LIKE po numerze nie znajdzie klienta.
    var customer = new Customer(
        Guid.NewGuid(), "John", "Doe", "john@example.com",
        new PhoneNumber("+48509123456"), "");

    Assert.Equal("48509123456", customer.PhoneNumberSearch);

    customer.Update("John", "Doe", "john@example.com", new PhoneNumber("+48600700800"), "");
    Assert.Equal("48600700800", customer.PhoneNumberSearch);

    customer.Anonymize();
    Assert.Null(customer.PhoneNumber);
    Assert.Null(customer.PhoneNumberSearch);
  }

  [Fact]
  public void Update_ShouldUpdateValuesCorrectly()
  {
    // Arrange
    var customer = new Customer(
        Guid.NewGuid(),
        "Old",
        "Name",
        "old@example.com",
        new PhoneNumber("+48501111222"),
        "");
    var newFirstName = "New";
    var newLastName = "Name";
    var newEmail = "new@example.com";
    var newPhone = new PhoneNumber("+48502222333");
    var newNotes = "Updated notes";

    // Act
    customer.Update(newFirstName, newLastName, newEmail, newPhone, newNotes);

    // Assert
    Assert.Equal(newFirstName, customer.FirstName);
    Assert.Equal(newLastName, customer.LastName);
    Assert.Equal(newEmail, customer.Email);
    Assert.Equal(newPhone.Value, customer.PhoneNumber.Value);
    Assert.Equal(newNotes, customer.GeneralNotes);
  }

  [Fact]
  public void Constructor_AllowsMissingEmailAndPhone()
  {
    var customer = new Customer(Guid.NewGuid(), "Jan", "Kowalski", "", null, "");

    Assert.Equal(string.Empty, customer.Email);
    Assert.Null(customer.PhoneNumber);
    Assert.Null(customer.PhoneNumberSearch);
  }

  [Fact]
  public void Update_AllowsClearingEmailAndPhone()
  {
    var customer = new Customer(
        Guid.NewGuid(), "Jan", "Kowalski", "jan@example.com",
        new PhoneNumber("+48501234567"), "");

    customer.Update("Jan", "Kowalski", "", null, "");

    Assert.Equal(string.Empty, customer.Email);
    Assert.Null(customer.PhoneNumber);
    Assert.Null(customer.PhoneNumberSearch);
  }

  [Theory]
  [InlineData("not-a-phone")]
  public void Update_WithInvalidPhone_ShouldThrowException(string phone)
  {
    // Arrange
    var customer = new Customer(
        Guid.NewGuid(),
        "First",
        "Last",
        "email@example.com",
        new PhoneNumber("+48503333444"),
        "");

    // Act & Assert
    Assert.ThrowsAny<Exception>(() =>
        customer.Update("First", "Last", "email@example.com", new PhoneNumber(phone), ""));
  }

  [Theory]
  [InlineData("@anna_nails", "anna_nails")]
  [InlineData("  @anna_nails  ", "anna_nails")]
  [InlineData("anna_nails", "anna_nails")]
  public void CreateFromPublicBooking_NormalizesInstagramNick(string input, string expected)
  {
    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null, instagramNick: input);

    Assert.Equal(expected, customer.InstagramNick);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("@")]
  public void CreateFromPublicBooking_BlankInstagramNick_IsNull(string? input)
  {
    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null, instagramNick: input);

    Assert.Null(customer.InstagramNick);
  }

  [Fact]
  public void CreateFromPublicBooking_TruncatesInstagramNickTo30Chars()
  {
    var longNick = new string('a', 50);

    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null, instagramNick: longNick);

    Assert.Equal(30, customer.InstagramNick!.Length);
  }

  [Fact]
  public void EnrichContactFromPublicBooking_FillsInstagramNickOnlyWhenEmpty()
  {
    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null, instagramNick: "first_nick");

    // Nie nadpisujemy istniejącego nicku (dane zadbane przez salon).
    customer.EnrichContactFromPublicBooking(null, null, instagramNick: "second_nick");
    Assert.Equal("first_nick", customer.InstagramNick);
  }

  [Fact]
  public void EnrichContactFromPublicBooking_SetsInstagramNickWhenPreviouslyEmpty()
  {
    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null);
    Assert.Null(customer.InstagramNick);

    customer.EnrichContactFromPublicBooking(null, null, instagramNick: "@new_nick");
    Assert.Equal("new_nick", customer.InstagramNick);
  }

  [Fact]
  public void Anonymize_ClearsInstagramNick()
  {
    var customer = Customer.CreateFromPublicBooking(
        Guid.NewGuid(), "anna@example.com", null, instagramNick: "anna_nails");

    customer.Anonymize();

    Assert.Null(customer.InstagramNick);
  }
}
