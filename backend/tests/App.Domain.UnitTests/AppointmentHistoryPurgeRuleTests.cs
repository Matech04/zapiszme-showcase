using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Services;

namespace App.Domain.UnitTests;

/// <summary>
/// NO-HISTORY-DOMAIN — reguła <see cref="AppointmentCleanupService.ShouldPurgeForNoHistoryRetention"/>:
/// usuwamy WYŁĄCZNIE wizyty terminalne (Completed/Canceled) z datą w przeszłości; nigdy bieżące/przyszłe
/// ani nie-terminalne (Pending/AwaitingOtp/Booked/InProgress).
/// </summary>
public sealed class AppointmentHistoryPurgeRuleTests
{
  private static readonly DateOnly Today = new(2026, 6, 24);
  private static readonly DateOnly Yesterday = Today.AddDays(-1);
  private static readonly DateOnly Tomorrow = Today.AddDays(1);

  [Theory]
  [MemberData(nameof(TerminalStatuses))]
  public void Terminal_status_in_the_past_is_purged(AppointmentStatus status)
  {
    Assert.True(AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(status, Yesterday, Today));
  }

  [Theory]
  [MemberData(nameof(TerminalStatuses))]
  public void Terminal_status_today_is_kept(AppointmentStatus status)
  {
    Assert.False(AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(status, Today, Today));
  }

  [Theory]
  [MemberData(nameof(TerminalStatuses))]
  public void Terminal_status_in_the_future_is_kept(AppointmentStatus status)
  {
    Assert.False(AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(status, Tomorrow, Today));
  }

  [Theory]
  [MemberData(nameof(NonTerminalStatuses))]
  public void Non_terminal_status_in_the_past_is_kept(AppointmentStatus status)
  {
    // Aktywne holdy i wizyty w toku/zarezerwowane NIE są historią — nie ruszamy ich,
    // nawet gdy (przejściowo) niosą przeszłą datę. Anti-abuse holdów zostaje nietknięte.
    Assert.False(AppointmentCleanupService.ShouldPurgeForNoHistoryRetention(status, Yesterday, Today));
  }

  public static TheoryData<AppointmentStatus> TerminalStatuses() => new()
  {
    AppointmentStatus.Completed,
    AppointmentStatus.Canceled,
  };

  public static TheoryData<AppointmentStatus> NonTerminalStatuses() => new()
  {
    AppointmentStatus.Pending,
    AppointmentStatus.AwaitingOtp,
    AppointmentStatus.Booked,
    AppointmentStatus.InProgress,
  };
}
