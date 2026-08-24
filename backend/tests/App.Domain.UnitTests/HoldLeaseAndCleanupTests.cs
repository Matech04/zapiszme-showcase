using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Services;

namespace App.Domain.UnitTests;

/// <summary>
/// HOLD-001/002 — HoldLease.IsExpired / IsValid.
/// HOLD-004 — AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease.
/// </summary>
public sealed class HoldLeaseAndCleanupTests
{
  // HOLD-001 HappyPath: IsExpired false przed ExpiryTimeUtc
  [Fact]
  public void IsExpired_returns_false_before_expiry()
  {
    var lease = new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(10));

    Assert.False(lease.IsExpired());
  }

  // HOLD-001 Negative: IsExpired true po ExpiryTimeUtc
  [Fact]
  public void IsExpired_returns_true_after_expiry()
  {
    var lease = new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1));

    Assert.True(lease.IsExpired());
  }

  // HOLD-002 HappyPath: IsValid true dla pasującego niezagasłego tokena
  [Fact]
  public void IsValid_returns_true_for_matching_token_on_active_lease()
  {
    var token = Guid.NewGuid();
    var lease = new HoldLease(token, DateTime.UtcNow.AddMinutes(10));

    Assert.True(lease.IsValid(token));
  }

  // HOLD-002 Negative: IsValid false dla wygasłego (nawet z dobrym tokenem)
  [Fact]
  public void IsValid_returns_false_when_lease_is_expired_even_with_correct_token()
  {
    var token = Guid.NewGuid();
    var lease = new HoldLease(token, DateTime.UtcNow.AddMinutes(-1));

    Assert.False(lease.IsValid(token));
  }

  // HOLD-002 Negative: IsValid false dla złego tokena
  [Fact]
  public void IsValid_returns_false_for_wrong_token_on_active_lease()
  {
    var lease = new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(10));

    Assert.False(lease.IsValid(Guid.NewGuid()));
  }

  // HOLD-004 HappyPath: ShouldRemovePending true dla Pending z wygasłą dzierżawą
  [Fact]
  public void ShouldRemove_returns_true_for_pending_with_expired_lease()
  {
    var appt = BuildAppointment(AppointmentStatus.Pending, lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5)));

    var result = AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, DateTime.UtcNow);

    Assert.True(result);
  }

  // HOLD-006: ShouldRemovePending true dla AwaitingOtp z wygasłą dzierżawą
  [Fact]
  public void ShouldRemove_returns_true_for_awaiting_otp_with_expired_lease()
  {
    var appt = BuildAppointment(AppointmentStatus.AwaitingOtp, lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1)));

    var result = AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, DateTime.UtcNow);

    Assert.True(result);
  }

  // HOLD-004 Negative: ShouldRemovePending false dla Booked/Completed/Canceled
  [Theory]
  [InlineData("Booked")]
  [InlineData("Completed")]
  [InlineData("Canceled")]
  [InlineData("InProgress")]
  public void ShouldRemove_returns_false_for_non_pending_statuses_regardless_of_lease(string statusName)
  {
    var status = AppointmentStatus.FromName(statusName);
    var appt = BuildAppointment(status, lease: new HoldLease(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10)));

    var result = AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, DateTime.UtcNow);

    Assert.False(result);
  }

  // HOLD-004 KRYTYCZNE Negative: ShouldRemovePending false dla Pending BEZ dzierżawy (staff-created)
  [Fact]
  public void ShouldRemove_returns_false_for_pending_with_no_lease_staff_created()
  {
    var appt = BuildAppointment(AppointmentStatus.Pending, lease: null);

    var result = AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, DateTime.UtcNow);

    Assert.False(result);
  }

  // HOLD-004 EdgeCase: dzierżawa wygasa dokładnie teraz — boundary
  [Fact]
  public void ShouldRemove_returns_false_at_exact_expiry_boundary()
  {
    var expiry = DateTime.UtcNow;
    var appt = BuildAppointment(AppointmentStatus.Pending, lease: new HoldLease(Guid.NewGuid(), expiry));

    var result = AppointmentCleanupService.ShouldRemovePendingDueToExpiredHoldLease(appt, expiry);

    // utcNow > Lease.ExpiryTimeUtc — przy równości false (nie strict greater)
    Assert.False(result);
  }

  private static Appointment BuildAppointment(AppointmentStatus status, HoldLease? lease)
  {
    return new Appointment(
      tenantId: Guid.NewGuid(),
      employeeId: Guid.NewGuid(),
      serviceId: Guid.NewGuid(),
      customerId: null,
      date: new DateOnly(2026, 6, 1),
      startTime: new TimeOnly(10, 0),
      endTime: new TimeOnly(10, 30),
      status: status,
      totalPrice: new Money(80m, "PLN"),
      appointmentNotes: string.Empty,
      lease: lease);
  }
}
