namespace App.Domain.Common;

public static class Guard
{
  public static void AgainstEmptyString(string value, string fieldName)
  {
    if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{fieldName} cannot be empty", fieldName);
  }

  public static void AgainstNegative(decimal number, string fieldName)
  {
    if (number < 0) throw new ArgumentException($"{fieldName} must be a positive number", fieldName);
  }

  public static string ReplaceSpaces(string text)
  {
    return NormalizeRequiredText(text, nameof(text)).ToLowerInvariant().Replace(" ", "-");
  }

  public static string NormalizeRequiredText(string value, string fieldName)
  {
    AgainstEmptyString(value, fieldName);
    return value.Trim();
  }

  public static string NormalizeOptionalText(string? value)
  {
    return value?.Trim() ?? string.Empty;
  }

  public static string NormalizeEmail(string email)
  {
    var normalized = NormalizeRequiredText(email, nameof(email));
    AgainstInvalidEmail(normalized);
    return normalized.ToLowerInvariant();
  }

  public static void AgainstInvalidRange(decimal value, decimal min, decimal max, string name)
  {
    if (value < min || value > max)
    {
      throw new ArgumentException($"{value} is not in accepted range", name);
    }
  }

  public static void AgainstInvalidTimeRange(TimeOnly startTime, TimeOnly endTime)
  {
    if (startTime >= endTime)
    {
      throw new InvalidTimeRangeException();
    }
  }

  public static void AgainstInvalidAppointmentSlotStepMinutes(int value)
  {
    if (value < 1 || value > 240)
    {
      throw new ArgumentException("Appointment slot step must be between 1 and 240 minutes.", nameof(value));
    }
  }

  public static void AgainstInvalidYearMonth(int year, int month)
  {
    if (year < 2000 || year > 9999)
    {
      throw new ArgumentException($"Year {year} is out of accepted range (2000-9999).", nameof(year));
    }

    if (month < 1 || month > 12)
    {
      throw new ArgumentException($"Month {month} is out of accepted range (1-12).", nameof(month));
    }
  }

  /// <summary>
  /// Horyzont rezerwacji online — ile dni naprzód klient może w ogóle zobaczyć terminy.
  /// Górna granica (5 lat) chroni przed „bez limitu" wpisanym przez pomyłkę.
  /// </summary>
  public static void AgainstInvalidBookingHorizonDays(int value)
  {
    if (value < 1 || value > 1826)
    {
      throw new ArgumentException("Booking horizon must be between 1 and 1826 days.", nameof(value));
    }
  }

  public static bool NotNull(object? obj, string name)
  {
    if (obj != null)
    {
      return true;
    }
    else
    {
      throw new ArgumentException($"{name} dosen't exist", name);
    }
  }

  public static bool AgainstInvalidEmail(string email)
  {
    try
    {
      // Wykorzystanie systemowej klasy do parsowania adresu
      var addr = new System.Net.Mail.MailAddress(email);
      return addr.Address == email;
    }
    catch
    {
      throw new ArgumentException("Invalid email", nameof(email));
    }
  }
}