using App.Application.Appointments;

namespace App.Application.UnitTests;

/// <summary>
/// Testy DST dla `AppointmentScheduleGuard.ToUtcInstant` w strefie `Europe/Warsaw`.
/// W Polsce DST działa standardowo: ostatnia niedziela marca o 02:00 → +1h
/// (czas letni), ostatnia niedziela października o 03:00 → -1h (czas zimowy).
/// </summary>
public class AppointmentScheduleGuardDstTests
{
  private const string TimeZone = "Europe/Warsaw";

  [Fact]
  public void ToUtcInstant_BeforeDstSpringForward_UsesWinterOffset()
  {
    // 30.03.2026 jest niedzielą zmiany czasu — ale o 01:30 to jeszcze CET (+01:00).
    var date = new DateOnly(2026, 3, 29);
    var time = new TimeOnly(1, 30);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(DateTimeKind.Utc, utc.Kind);
    Assert.Equal(new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc), utc);
  }

  [Fact]
  public void ToUtcInstant_AfterDstSpringForward_UsesSummerOffset()
  {
    // 30.03.2026 03:30 (po przeskoku) — Europe/Warsaw to CEST (+02:00).
    var date = new DateOnly(2026, 3, 29);
    var time = new TimeOnly(4, 30);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Utc), utc);
  }

  [Fact]
  public void ToUtcInstant_BeforeDstFallBack_UsesSummerOffset()
  {
    // 25.10.2026 01:30 (jednoznacznie przed ambiguous oknem 02:00–03:00) — CEST (+02:00).
    // Czas 02:30 byłby ambiguous (.NET wybiera standard offset → CET), więc użyto 01:30.
    var date = new DateOnly(2026, 10, 25);
    var time = new TimeOnly(1, 30);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc), utc);
  }

  [Fact]
  public void ToUtcInstant_AfterDstFallBack_UsesWinterOffset()
  {
    // 25.10.2026 03:30 (po przeskoku) — CET (+01:00).
    var date = new DateOnly(2026, 10, 25);
    var time = new TimeOnly(4, 30);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(new DateTime(2026, 10, 25, 3, 30, 0, DateTimeKind.Utc), utc);
  }

  [Fact]
  public void ToUtcInstant_OffSeasonSummer_UsesSummerOffset()
  {
    // 15.07.2026 12:00 — pewne CEST (+02:00).
    var date = new DateOnly(2026, 7, 15);
    var time = new TimeOnly(12, 0);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc), utc);
  }

  [Fact]
  public void ToUtcInstant_OffSeasonWinter_UsesWinterOffset()
  {
    // 15.01.2026 12:00 — pewne CET (+01:00).
    var date = new DateOnly(2026, 1, 15);
    var time = new TimeOnly(12, 0);

    var utc = AppointmentScheduleGuard.ToUtcInstant(TimeZone, date, time);

    Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc), utc);
  }
}
