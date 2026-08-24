using App.Application.Appointments;
using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Application.UnitTests;

public class AppointmentStatusLifecycleRulesTests
{
  private static readonly DateTime Start = new(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);
  private static readonly DateTime End = new(2026, 4, 17, 11, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void ResolveTransition_AfterEnd_Pending_To_Canceled()
  {
    var next = AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Pending,
      Start,
      End,
      End.AddMinutes(1));

    Assert.Equal(AppointmentStatus.Canceled, next);
  }

  [Fact]
  public void ResolveTransition_AfterEnd_Booked_To_Completed()
  {
    var next = AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Booked,
      Start,
      End,
      End);

    Assert.Equal(AppointmentStatus.Completed, next);
  }

  [Fact]
  public void ResolveTransition_AfterEnd_InProgress_To_Completed()
  {
    var next = AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.InProgress,
      Start,
      End,
      End.AddSeconds(1));

    Assert.Equal(AppointmentStatus.Completed, next);
  }

  [Fact]
  public void ResolveTransition_BeforeEnd_AfterStart_Booked_To_InProgress()
  {
    var next = AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Booked,
      Start,
      End,
      Start);

    Assert.Equal(AppointmentStatus.InProgress, next);
  }

  [Fact]
  public void ResolveTransition_BeforeStart_Booked_Stays()
  {
    var next = AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Booked,
      Start,
      End,
      Start.AddMinutes(-1));

    Assert.Null(next);
  }

  [Fact]
  public void ResolveTransition_Terminal_NoChange()
  {
    Assert.Null(AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Completed, Start, End, End.AddDays(1)));

    Assert.Null(AppointmentStatusLifecycleRules.ResolveTransition(
      AppointmentStatus.Canceled, Start, End, End.AddDays(1)));
  }
}
