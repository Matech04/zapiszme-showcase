using PhoneNumbers; // Z pakietu NuGet: libphonenumber-csharp

public record PhoneNumber
{
  public string Value { get; }

  // Możesz łatwo udostępnić te dane, jeśli są potrzebne
  public int CountryCode { get; }
  public ulong NationalNumber { get; }

  /// <summary>
  /// Polski numer (+48). ICP produktu = kosmetyka SOLO w Polsce, więc numery zagraniczne są
  /// nielegitne. Ścieżki wysyłające realny SMS (rejestracja, OTP klienta) odrzucają numery
  /// spoza +48 — twarda bariera przeciw toll-fraud / SMS-pumping na numery premium/zagraniczne.
  /// </summary>
  public bool IsPolish => CountryCode == 48;

  /// <summary>
  /// Normalizuje tekst do samych cyfr (np. E164 <c>+48509123456</c> -> <c>48509123456</c>). Wspólne
  /// dla zapisu kolumny <c>Customer.PhoneNumberSearch</c> i dla frazy wyszukiwania — kolumna numeru ma
  /// value converter, którego EF nie tłumaczy na <c>LIKE</c>, więc szukamy po zwykłym <c>string</c>.
  /// Metoda (nie property), by nie wyciekać do kontraktu API przez NSwag.
  /// </summary>
  public static string DigitsOnly(string? input) =>
    string.IsNullOrEmpty(input) ? string.Empty : new string(input.Where(char.IsDigit).ToArray());

  // Domyślny region przydaje się, gdy użytkownik wpisze "123456789" bez "+48"
  public PhoneNumber(string rawNumber, string defaultRegion = "PL")
  {
    var phoneNumberUtil = PhoneNumberUtil.GetInstance();

    try
    {
      // Biblioteka sama parsuje, oddziela kod kraju i radzi sobie z formatowaniem
      var parsedNumber = phoneNumberUtil.Parse(rawNumber, defaultRegion);

      if (!phoneNumberUtil.IsValidNumber(parsedNumber))
      {
        throw new ArgumentException("Podany numer telefonu jest nieprawidłowy dla danego regionu.");
      }

      Value = phoneNumberUtil.Format(parsedNumber, PhoneNumberFormat.E164);
      CountryCode = parsedNumber.CountryCode;
      NationalNumber = parsedNumber.NationalNumber;
    }
    catch (NumberParseException ex)
    {
      throw new ArgumentException($"Błąd parsowania numeru: {ex.Message}");
    }
  }
}