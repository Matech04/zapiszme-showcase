using App.Domain.Exceptions;

namespace App.Application.Appointments;

/// <summary>
/// Wizyty są planowane w „czasie salonu” (kalendarz i godziny jak na recepcji), nie w UTC serwera.
/// </summary>
public static class AppointmentScheduleGuard
{
  public static void EnsureStartNotInPast(string timeZoneId, DateOnly date, TimeOnly startTime)
  {
    if (IsStartInPast(timeZoneId, date, startTime))
    {
      throw new AppointmentBookingRuleException(
        "Nie można zapisać wizyty w przeszłości. Wybierz późniejszy termin.");
    }
  }

  public static void EnsureStartInPast(string timeZoneId, DateOnly date, TimeOnly startTime)
  {
    if (!IsStartInPast(timeZoneId, date, startTime))
    {
      throw new AppointmentBookingRuleException(
        "Wizyta zakończona musi mieć datę i godzinę w przeszłości.");
    }
  }

  /// <summary>
  /// Prawda, jeśli start wizyty (data + godzina w strefie biznesowej) jest wcześniejszy niż bieżący moment UTC.
  /// </summary>
  /// <summary>
  /// Konwersja „czasu na kalendarzu salonu” (data + godzina bez strefy) do UTC.
  /// </summary>
  public static DateTime ToUtcInstant(string timeZoneId, DateOnly date, TimeOnly time)
  {
    var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    var localUnspecified = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
    return TimeZoneInfo.ConvertTimeToUtc(localUnspecified, tz);
  }

  public static bool IsStartInPast(string timeZoneId, DateOnly date, TimeOnly startTime)
  {
    return ToUtcInstant(timeZoneId, date, startTime) < DateTime.UtcNow;
  }

  public static DateOnly TodayInBusinessTimeZone(string timeZoneId)
      => TodayInBusinessTimeZone(timeZoneId, DateTime.UtcNow);

  /// <summary>
  /// „Dziś" w strefie biznesowej liczone z PODANEGO instantu UTC (a nie realnego zegara). Pozwala
  /// jobom honorować wstrzyknięty <see cref="TimeProvider"/> — deterministyczne testy i sterowanie
  /// czasem w E2E. W produkcji wołane z realnym <c>GetUtcNow()</c>, więc wynik jest ten sam.
  /// </summary>
  public static DateOnly TodayInBusinessTimeZone(string timeZoneId, DateTime utcNow)
  {
    var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
    return DateOnly.FromDateTime(local);
  }
}
