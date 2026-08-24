using App.Domain.Aggregates.CustomerAggregate;

namespace App.Domain.UnitTests;

/// <summary>
/// CUST-006 — Customer.CreateFromPublicBooking (wymagania kontaktu i źródło).
/// CUST-007 — Customer.EnrichContactFromPublicBooking (uzupełnianie, nie nadpisywanie).
/// CUST-008 — Customer.Whitelist / Unwhitelist.
/// </summary>
public class CustomerPublicBookingTests
{
  // CUST-006: phone only → success, Source = PublicBooking
  [Fact]
  public void CreateFromPublicBooking_WithPhoneOnly_CreatesCustomerWithPublicBookingSource()
  {
    var tenantId = Guid.NewGuid();
    var phone = new PhoneNumber("+48501234567");

    var customer = Customer.CreateFromPublicBooking(tenantId, null, phone);

    Assert.Equal(tenantId, customer.TenantId);
    Assert.Equal(CustomerSource.PublicBooking, customer.Source);
    Assert.Equal(phone, customer.PhoneNumber);
    Assert.True(string.IsNullOrEmpty(customer.Email));
    Assert.NotEqual(Guid.Empty, customer.Id);
  }

  // CUST-006: email only → success, Source = PublicBooking
  [Fact]
  public void CreateFromPublicBooking_WithEmailOnly_CreatesCustomerWithPublicBookingSource()
  {
    var tenantId = Guid.NewGuid();
    const string email = "test@example.com";

    var customer = Customer.CreateFromPublicBooking(tenantId, email, null);

    Assert.Equal(tenantId, customer.TenantId);
    Assert.Equal(CustomerSource.PublicBooking, customer.Source);
    Assert.Null(customer.PhoneNumber);
    Assert.False(string.IsNullOrEmpty(customer.Email));
  }

  // CUST-006: no phone, no email → ArgumentException
  [Fact]
  public void CreateFromPublicBooking_WithNoContactData_ThrowsArgumentException()
  {
    Assert.Throws<ArgumentException>(() =>
      Customer.CreateFromPublicBooking(Guid.NewGuid(), null, null));
  }

  // CUST-006: whitespace email and null phone → ArgumentException
  [Fact]
  public void CreateFromPublicBooking_WithBlankEmailAndNullPhone_ThrowsArgumentException()
  {
    Assert.Throws<ArgumentException>(() =>
      Customer.CreateFromPublicBooking(Guid.NewGuid(), "   ", null));
  }

  // CUST-006: imię i nazwisko z formularza rezerwacji są zapisane (przycięte) na kliencie
  [Fact]
  public void CreateFromPublicBooking_WithName_StoresTrimmedFirstAndLastName()
  {
    var customer = Customer.CreateFromPublicBooking(
      Guid.NewGuid(), "mail@example.com", null, "  Anna ", " Kowalska ");

    Assert.Equal("Anna", customer.FirstName);
    Assert.Equal("Kowalska", customer.LastName);
  }

  // CUST-006: bez imienia/nazwiska (salon ich nie wymaga) — pola pozostają puste
  [Fact]
  public void CreateFromPublicBooking_WithoutName_LeavesNamesEmpty()
  {
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), "mail@example.com", null);

    Assert.Equal(string.Empty, customer.FirstName);
    Assert.Equal(string.Empty, customer.LastName);
  }

  // CUST-007: uzupełnia puste imię/nazwisko z nowej rezerwacji
  [Fact]
  public void EnrichContactFromPublicBooking_FillsMissingName()
  {
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), "mail@example.com", null);

    customer.EnrichContactFromPublicBooking(null, null, "Anna", "Kowalska");

    Assert.Equal("Anna", customer.FirstName);
    Assert.Equal("Kowalska", customer.LastName);
  }

  // CUST-007: NIE nadpisuje imienia/nazwiska zadbanego przez salon
  [Fact]
  public void EnrichContactFromPublicBooking_DoesNotOverwriteExistingName()
  {
    var customer = new Customer(Guid.NewGuid(), "Jan", "Nowak", "jan@example.com",
      new PhoneNumber("+48501000001"), "");

    customer.EnrichContactFromPublicBooking(null, null, "Anna", "Kowalska");

    Assert.Equal("Jan", customer.FirstName);
    Assert.Equal("Nowak", customer.LastName);
  }

  // CUST-007: adds missing phone when existing phone is null
  [Fact]
  public void EnrichContactFromPublicBooking_AddsMissingPhone()
  {
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), "mail@example.com", null);
    var phone = new PhoneNumber("+48501111222");

    customer.EnrichContactFromPublicBooking(null, phone);

    Assert.Equal(phone, customer.PhoneNumber);
  }

  // CUST-007: adds missing email when existing email is empty
  [Fact]
  public void EnrichContactFromPublicBooking_AddsMissingEmail()
  {
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), null, new PhoneNumber("+48501111222"));

    customer.EnrichContactFromPublicBooking("new@example.com", null);

    Assert.False(string.IsNullOrEmpty(customer.Email));
  }

  // CUST-007: does NOT overwrite existing phone
  [Fact]
  public void EnrichContactFromPublicBooking_DoesNotOverwriteExistingPhone()
  {
    var existingPhone = new PhoneNumber("+48501111111");
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), null, existingPhone);
    var newPhone = new PhoneNumber("+48502222222");

    customer.EnrichContactFromPublicBooking(null, newPhone);

    Assert.Equal(existingPhone, customer.PhoneNumber);
  }

  // CUST-007: does NOT overwrite existing email
  [Fact]
  public void EnrichContactFromPublicBooking_DoesNotOverwriteExistingEmail()
  {
    var customer = Customer.CreateFromPublicBooking(Guid.NewGuid(), "original@example.com", null);

    customer.EnrichContactFromPublicBooking("new@example.com", null);

    Assert.Equal("original@example.com", customer.Email);
  }

  // CUST-008: Whitelist → IsWhitelisted = true
  [Fact]
  public void Whitelist_SetsIsWhitelistedTrue()
  {
    var customer = new Customer(Guid.NewGuid(), "Jan", "Kowalski", "jan@example.com",
      new PhoneNumber("+48501000001"), "");

    customer.Whitelist();

    Assert.True(customer.IsWhitelisted);
  }

  // CUST-008: Unwhitelist → IsWhitelisted = false
  [Fact]
  public void Unwhitelist_SetsIsWhitelistedFalse()
  {
    var customer = new Customer(Guid.NewGuid(), "Jan", "Kowalski", "jan@example.com",
      new PhoneNumber("+48501000001"), "");
    customer.Whitelist();

    customer.Unwhitelist();

    Assert.False(customer.IsWhitelisted);
  }

  // CUST-008: Whitelist twice → idempotent
  [Fact]
  public void Whitelist_CalledTwice_IsIdempotent()
  {
    var customer = new Customer(Guid.NewGuid(), "Jan", "Kowalski", "jan@example.com",
      new PhoneNumber("+48501000001"), "");

    customer.Whitelist();
    customer.Whitelist();

    Assert.True(customer.IsWhitelisted);
  }
}
