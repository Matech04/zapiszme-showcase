using App.Domain.Common;

namespace App.Domain.Aggregates.CustomerAggregate;

public class Customer : Entity, ITenantEntity, IAggregateRoot, ISoftDelete
{
  public Guid TenantId { get; private set; }
  public string FirstName { get; private set; } = string.Empty;
  public string LastName { get; private set; } = string.Empty;
  public string Email { get; private set; } = string.Empty;
  public PhoneNumber? PhoneNumber { get; private set; }

  /// <summary>
  /// Numer w postaci samych cyfr (np. <c>48509123456</c>) — utrzymywany automatycznie przy każdej
  /// zmianie <see cref="PhoneNumber"/>. Istnieje wyłącznie po to, by wyszukiwarka klientów mogła
  /// szukać fragmentem przez <c>LIKE</c>; kolumna <see cref="PhoneNumber"/> ma value converter,
  /// którego EF nie tłumaczy na <c>LIKE</c>. Nie ustawiać ręcznie spoza <see cref="SetPhoneNumber"/>.
  /// </summary>
  public string? PhoneNumberSearch { get; private set; }

  public DateOnly CreatedAt { get; private set; }
  public string GeneralNotes { get; private set; } = string.Empty;

  /// <summary>
  /// Nick klienta na Instagramie (bez wiodącego <c>@</c>), opcjonalny. Zbierany w publicznej
  /// rezerwacji gdy salon włączył <see cref="TenantAggregate.Tenant.CollectInstagramHandle"/>.
  /// Dla ICP (ruch z DM) pozwala salonowi odpisać klientce na IG. <c>null</c> gdy niepodany.
  /// </summary>
  public string? InstagramNick { get; private set; }

  public bool IsActive { get; private set; } = true;

  /// <summary>Pochodzenie rekordu — patrz <see cref="CustomerSource"/>.</summary>
  public CustomerSource Source { get; private set; } = CustomerSource.Manual;

  /// <summary>
  /// Czy klient należy do whitelisty rezerwacji (tryb salonu „tylko zaproszeni”).
  /// Domyślnie false. Toggle z poziomu CRM.
  /// </summary>
  public bool IsWhitelisted { get; private set; }

  public Customer(Guid tenantId, string firstName, string lastName, string email, PhoneNumber? phoneNumber, string generalNotes, string? instagramNick = null)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow);
    Update(firstName, lastName, email, phoneNumber, generalNotes, instagramNick);
  }

  private Customer() { }

  /// <summary>Jedyne miejsce zmiany numeru — trzyma <see cref="PhoneNumber"/> i pochodną
  /// <see cref="PhoneNumberSearch"/> w spójności.</summary>
  private void SetPhoneNumber(PhoneNumber? phoneNumber)
  {
    PhoneNumber = phoneNumber;
    PhoneNumberSearch = phoneNumber is null ? null : PhoneNumber.DigitsOnly(phoneNumber.Value);
  }

  public void Deactivate()
  {
    IsActive = false;
  }

  /// <summary>Normalizuje nick IG: trim, usuwa wiodące <c>@</c>, przycina do 30 znaków
  /// (limit nazwy użytkownika Instagrama). Puste/whitespace → <c>null</c>.</summary>
  private static string? NormalizeInstagramNick(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var trimmed = value.Trim().TrimStart('@').Trim();
    if (trimmed.Length == 0)
    {
      return null;
    }

    return trimmed.Length > 30 ? trimmed[..30] : trimmed;
  }

  public void Update(string firstName, string lastName, string email, PhoneNumber? phoneNumber, string generalNotes, string? instagramNick = null)
  {
    // Imię/nazwisko są opcjonalne — tożsamość klienta niosą równie dobrze e-mail/telefon (tak powstają
    // rekordy z publicznej rezerwacji i CreateQuickFromPanel). Wymóg „przynajmniej jeden identyfikator”
    // egzekwują walidatory komend (CRM), nie domena.
    FirstName = Guard.NormalizeOptionalText(firstName);
    LastName = Guard.NormalizeOptionalText(lastName);
    Email = string.IsNullOrWhiteSpace(email) ? string.Empty : Guard.NormalizeEmail(email);
    SetPhoneNumber(phoneNumber);
    GeneralNotes = Guard.NormalizeOptionalText(generalNotes);
    InstagramNick = NormalizeInstagramNick(instagramNick);
  }

  public void ChangeNotes(string newNotes)
  {
    GeneralNotes = Guard.NormalizeOptionalText(newNotes);
  }

  /// <summary>
  /// Tworzy klienta-szkielet po publicznej rezerwacji. Wymaga przynajmniej jednego
  /// kanału kontaktu (email lub phone). Imię/nazwisko są puste, chyba że salon wymaga
  /// ich podania (<see cref="TenantAggregate.Tenant.RequireCustomerName"/>) — wtedy
  /// przekazywane są tu z formularza rezerwacji.
  /// </summary>
  public static Customer CreateFromPublicBooking(
    Guid tenantId,
    string? email,
    PhoneNumber? phoneNumber,
    string? firstName = null,
    string? lastName = null,
    string? instagramNick = null)
  {
    if (phoneNumber is null && string.IsNullOrWhiteSpace(email))
    {
      throw new ArgumentException("Public booking customer wymaga email albo phoneNumber.");
    }

    var customer = new Customer
    {
      Id = Guid.NewGuid(),
      TenantId = tenantId,
      CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
      FirstName = Guard.NormalizeOptionalText(firstName),
      LastName = Guard.NormalizeOptionalText(lastName),
      Email = string.IsNullOrWhiteSpace(email) ? string.Empty : Guard.NormalizeEmail(email),
      GeneralNotes = string.Empty,
      InstagramNick = NormalizeInstagramNick(instagramNick),
      IsActive = true,
      Source = CustomerSource.PublicBooking,
    };
    customer.SetPhoneNumber(phoneNumber);
    return customer;
  }

  /// <summary>
  /// Klient-szkielet zakładany przy szybkim zapisie wizyty z panelu, gdy pracownik podał tylko
  /// numer telefonu (bez wskazania istniejącego klienta). Imię/nazwisko puste — salon uzupełni w
  /// CRM. <see cref="CustomerSource.Manual"/>, bo rekord powstał z działania panelu, nie z publicznej
  /// rezerwacji. Numer jest wymagany (to jedyny nośnik tożsamości takiego rekordu).
  /// </summary>
  public static Customer CreateQuickFromPanel(Guid tenantId, PhoneNumber phoneNumber)
  {
    ArgumentNullException.ThrowIfNull(phoneNumber);

    var customer = new Customer
    {
      Id = Guid.NewGuid(),
      TenantId = tenantId,
      CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
      FirstName = string.Empty,
      LastName = string.Empty,
      Email = string.Empty,
      GeneralNotes = string.Empty,
      IsActive = true,
      Source = CustomerSource.Manual,
    };
    customer.SetPhoneNumber(phoneNumber);
    return customer;
  }

  /// <summary>
  /// Dla istniejącego klienta — uzupełnij brakujący kanał kontaktu po nowej publicznej rezerwacji.
  /// Imię/nazwisko uzupełniamy tylko gdy są puste (nie nadpisujemy danych zadbanych przez salon).
  /// </summary>
  public void EnrichContactFromPublicBooking(
    string? email,
    PhoneNumber? phoneNumber,
    string? firstName = null,
    string? lastName = null,
    string? instagramNick = null)
  {
    if (PhoneNumber is null && phoneNumber is not null)
    {
      SetPhoneNumber(phoneNumber);
    }
    if (string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(email))
    {
      Email = Guard.NormalizeEmail(email);
    }
    if (string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(firstName))
    {
      FirstName = Guard.NormalizeOptionalText(firstName);
    }
    if (string.IsNullOrWhiteSpace(LastName) && !string.IsNullOrWhiteSpace(lastName))
    {
      LastName = Guard.NormalizeOptionalText(lastName);
    }
    // Nick IG uzupełniamy tylko gdy pusty — nie nadpisujemy wartości zadbanej przez salon.
    if (string.IsNullOrWhiteSpace(InstagramNick) && !string.IsNullOrWhiteSpace(instagramNick))
    {
      InstagramNick = NormalizeInstagramNick(instagramNick);
    }
  }

  public void Whitelist() => IsWhitelisted = true;
  public void Unwhitelist() => IsWhitelisted = false;

  /// <summary>
  /// Realizacja prawa do bycia zapomnianym (RODO art. 17). Nadpisuje wszystkie dane osobowe
  /// (imię, nazwisko, e-mail, telefon, notatki) wartościami nieidentyfikującymi i dezaktywuje
  /// rekord. <see cref="Entity.Id"/> oraz FK z historii wizyt (<c>Appointment.CustomerId</c>)
  /// pozostają — wizyta bez danych osobowych nie jest już daną osobową, a FK
  /// <c>DeleteBehavior.Restrict</c> i tak blokuje fizyczne usunięcie. Operacja nieodwracalna.
  /// </summary>
  public void Anonymize()
  {
    FirstName = "Klient";
    LastName = "usunięty";
    Email = string.Empty;
    SetPhoneNumber(null);
    GeneralNotes = string.Empty;
    InstagramNick = null;
    IsWhitelisted = false;
    IsActive = false;
  }
}
