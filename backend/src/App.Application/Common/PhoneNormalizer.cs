namespace App.Application.Common;

public static class PhoneNormalizer
{
  /// <summary>
  /// Cyfry z numeru (do porównań i walidacji długości).
  /// </summary>
  public static string DigitsOnly(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
    {
      return string.Empty;
    }

    return new string(raw.Where(char.IsDigit).ToArray());
  }
}
