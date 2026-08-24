using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Domain.UnitTests;

/// <summary>Inwarianty AppointmentStatus: które stany są terminalne (podgląd inspiracji zbędny).</summary>
public class AppointmentStatusTests
{
  [Fact]
  public void Completed_and_Canceled_are_terminal()
  {
    Assert.True(AppointmentStatus.Completed.IsTerminal);
    Assert.True(AppointmentStatus.Canceled.IsTerminal);
  }

  [Fact]
  public void Non_terminal_states_are_not_terminal()
  {
    Assert.False(AppointmentStatus.Pending.IsTerminal);
    Assert.False(AppointmentStatus.Booked.IsTerminal);
    Assert.False(AppointmentStatus.InProgress.IsTerminal);
    Assert.False(AppointmentStatus.AwaitingOtp.IsTerminal);
  }
}
