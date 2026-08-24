using System.Text.RegularExpressions;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Normalizuje numery telefonów do formatu wymaganego przez smsapi.pl: <c>48xxxxxxxxx</c>
/// (cyfry, bez <c>+</c>, bez spacji/myślników). Obsługiwane wejścia:
/// <c>+48 501 234 567</c>, <c>48501234567</c>, <c>501234567</c>, <c>501-234-567</c>.
/// </summary>
public static class PhoneNumberNormalizer
{
  private static readonly Regex NonDigit = new(@"[^\d]", RegexOptions.Compiled);

  /// <summary>Zwraca <c>48xxxxxxxxx</c> dla polskich numerów. Rzuca <see cref="ArgumentException"/>.</summary>
  public static string ToSmsApiFormat(string? input)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      throw new ArgumentException("Numer telefonu jest pusty.", nameof(input));
    }

    var digits = NonDigit.Replace(input, string.Empty);

    if (digits.Length == 9)
    {
      return "48" + digits;
    }

    if (digits.Length == 11 && digits.StartsWith("48", StringComparison.Ordinal))
    {
      return digits;
    }

    throw new ArgumentException(
      $"Nieprawidłowy format numeru telefonu: '{input}'. Oczekiwany format PL: 9 cyfr lub 48xxxxxxxxx.",
      nameof(input));
  }

  /// <summary>Wersja non-throwing — zwraca <c>null</c> przy nieprawidłowym numerze.</summary>
  public static string? TryNormalize(string? input)
  {
    try
    {
      return ToSmsApiFormat(input);
    }
    catch (ArgumentException)
    {
      return null;
    }
  }
}
