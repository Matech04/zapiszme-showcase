using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Common;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Common;

/// <summary>
/// OTP-CLEANUP-001..004 — cykl usuwania wygasłych OTP (PII) po oknie retencji.
/// Statyczny entry-point <c>OtpCleanupHostedService.RunCycleAsync</c> leci bez kontekstu tenanta
/// (<c>IgnoreQueryFilters</c>). Obejmuje: SelfServiceOtp (hard delete), PhoneConfirmationOtp
/// (hard delete) i owned OtpVerification na wizycie (scrub kolumn, wiersz wizyty zostaje).
/// </summary>
public sealed class OtpCleanupTests
{
  private static readonly TimeSpan Retention = TimeSpan.FromHours(48);
  private static readonly DateTime UtcNow = new(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

  [Fact]
  public async Task Expired_self_service_otp_is_deleted()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    // ExpiresAtUtc = teraz - 49h (< cutoff = teraz - 48h).
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.com", "hash", UtcNow - TimeSpan.FromHours(49), TimeSpan.Zero);
    db.SelfServiceOtps.Add(otp);
    db.SaveChanges();

    var cleaned = await OtpCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(1, cleaned);
    Assert.False(await db.SelfServiceOtps.IgnoreQueryFilters().AnyAsync(o => o.Id == otp.Id, ct));
  }

  [Fact]
  public async Task Self_service_otp_within_retention_is_kept()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    // ExpiresAtUtc = teraz - 1h (> cutoff).
    var otp = SelfServiceOtp.ForEmail(Guid.NewGuid(), "a@b.com", "hash", UtcNow - TimeSpan.FromHours(1), TimeSpan.Zero);
    db.SelfServiceOtps.Add(otp);
    db.SaveChanges();

    var cleaned = await OtpCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(0, cleaned);
    Assert.True(await db.SelfServiceOtps.IgnoreQueryFilters().AnyAsync(o => o.Id == otp.Id, ct));
  }

  [Fact]
  public async Task Expired_phone_confirmation_otp_is_deleted()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    // Create wymaga dodatniego TTL — startujemy w przeszłości tak, by ExpiresAt < cutoff.
    var otp = PhoneConfirmationOtp.Create(Guid.NewGuid(), "hash", UtcNow - TimeSpan.FromHours(60), TimeSpan.FromHours(1));
    db.PhoneConfirmationOtps.Add(otp);
    db.SaveChanges();

    var cleaned = await OtpCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(1, cleaned);
    Assert.False(await db.PhoneConfirmationOtps.IgnoreQueryFilters().AnyAsync(o => o.Id == otp.Id, ct));
  }

  [Fact]
  public async Task Expired_appointment_otp_verification_is_scrubbed_but_row_kept()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var appointment = new Appointment(
      Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
      DateOnly.FromDateTime(UtcNow.AddDays(-1)), new TimeOnly(10, 0), new TimeOnly(11, 0),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, null);
    appointment.SetOtpVerification(
      OtpVerification.ForEmail("client@example.com", "123456", UtcNow - TimeSpan.FromHours(60)));
    db.Appointments.Add(appointment);
    db.SaveChanges();

    var cleaned = await OtpCleanupHostedService.RunCycleAsync(db, UtcNow, Retention, ct, NullLogger.Instance);

    Assert.Equal(1, cleaned);
    var reloaded = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(a => a.Id == appointment.Id, ct);
    Assert.Null(reloaded.OtpVerification);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static ApplicationDbContext SetupAnonymousDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new AnonymousCurrentTenantService());
  }

  private sealed class AnonymousCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
