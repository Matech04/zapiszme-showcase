using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

public class AppointmentDepositTests
{
  private static Appointment BookedAppointment() =>
    new(
      Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
      new DateOnly(2026, 6, 10), new TimeOnly(10, 0), new TimeOnly(11, 0),
      AppointmentStatus.Booked, new Money(100m, "PLN"), "notes", null);

  [Fact]
  public void GenerateDepositLink_ShouldSetAwaitingPaymentState()
  {
    var appt = BookedAppointment();
    var expiry = DateTime.UtcNow.AddHours(24);

    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_test_1", "https://pay/x", expiry, DateTime.UtcNow);

    Assert.Equal(AppointmentPaymentStatus.AwaitingPayment, appt.PaymentStatus);
    Assert.Equal(30m, appt.DepositAmount!.Amount);
    Assert.Equal("cs_test_1", appt.PaymentSessionId);
    Assert.Equal("https://pay/x", appt.PaymentLinkUrl);
    Assert.Equal(expiry, appt.LinkExpiresAtUtc);
    Assert.Null(appt.PaidAtUtc);
    // Status terminarza pozostaje nietknięty (ortogonalność).
    Assert.Equal(AppointmentStatus.Booked, appt.Status);
  }

  [Fact]
  public void ScrubPaymentLink_ShouldAlsoClearSendMarker()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
    appt.MarkDepositLinkSent(DateTime.UtcNow, "Sms");

    appt.ScrubPaymentLink();

    Assert.Null(appt.PaymentLinkUrl);
    Assert.Null(appt.PaymentSessionId);
    Assert.Null(appt.DepositLinkSentAtUtc);
    Assert.Null(appt.DepositLinkSentChannel);
  }

  [Fact]
  public void Regenerate_ShouldResetSendMarker()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
    appt.MarkDepositLinkSent(DateTime.UtcNow, "Sms");

    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_2", "https://pay/y", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    // Nowy link = nikt go jeszcze nie dostał; panel nie może pokazywać „wysłano".
    Assert.Null(appt.DepositLinkSentAtUtc);
    Assert.Null(appt.DepositLinkSentChannel);
  }

  [Fact]
  public void Regenerate_WhenUnpaid_ShouldOverwritePreviousLink()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_old", "https://pay/old", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    appt.GenerateDepositLink(new Money(40m, "PLN"), "cs_new", "https://pay/new", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    Assert.Equal("cs_new", appt.PaymentSessionId);
    Assert.Equal(40m, appt.DepositAmount!.Amount);
  }

  [Fact]
  public void MarkDepositPaid_ShouldBeIdempotent()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    appt.MarkDepositPaid(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
    var firstPaidAt = appt.PaidAtUtc;
    appt.MarkDepositPaid(new DateTime(2026, 6, 1, 13, 0, 0, DateTimeKind.Utc));

    Assert.Equal(AppointmentPaymentStatus.Paid, appt.PaymentStatus);
    Assert.Equal(firstPaidAt, appt.PaidAtUtc);
  }

  [Fact]
  public void GenerateDepositLink_AfterPaid_ShouldThrow()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
    appt.MarkDepositPaid(DateTime.UtcNow);

    var ex = Assert.Throws<AppointmentBookingRuleException>(() =>
      appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_2", "https://pay/y", DateTime.UtcNow.AddHours(24), DateTime.UtcNow));
    Assert.Equal(ErrorCodes.DepositAlreadyPaid, ex.ErrorCode);
  }

  [Fact]
  public void GenerateDepositLink_OnCanceledAppointment_ShouldThrow()
  {
    var appt = BookedAppointment();
    appt.ChangeStatus(AppointmentStatus.Canceled);

    var ex = Assert.Throws<AppointmentBookingRuleException>(() =>
      appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow));
    Assert.Equal(ErrorCodes.DepositOnTerminalAppointment, ex.ErrorCode);
  }

  [Fact]
  public void MarkDepositRefunded_WhenNotPaid_ShouldThrow()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    var ex = Assert.Throws<AppointmentBookingRuleException>(() => appt.MarkDepositRefunded());
    Assert.Equal(ErrorCodes.DepositNotPaid, ex.ErrorCode);
  }

  [Fact]
  public void MarkDepositRefunded_WhenPaid_ShouldSucceed()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
    appt.MarkDepositPaid(DateTime.UtcNow);

    appt.MarkDepositRefunded();

    Assert.Equal(AppointmentPaymentStatus.Refunded, appt.PaymentStatus);
  }

  [Fact]
  public void GenerateDepositLink_ShouldCountAttempts()
  {
    var appt = BookedAppointment();
    var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    Assert.Equal(0, appt.DepositLinkAttempts);

    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", now.AddHours(24), now);
    Assert.Equal(1, appt.DepositLinkAttempts);

    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_2", "https://pay/y", now.AddHours(25), now.AddHours(1));
    Assert.Equal(2, appt.DepositLinkAttempts);
  }

  [Fact]
  public void Regenerate_WhenPreviousLinkExpired_ShouldCountItAsExpired()
  {
    var appt = BookedAppointment();
    var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", now.AddHours(24), now);

    // Regeneracja już po wygaśnięciu poprzedniego linku — realna nieudana próba.
    var afterExpiry = now.AddHours(30);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_2", "https://pay/y", afterExpiry.AddHours(24), afterExpiry);

    Assert.Equal(1, appt.ExpiredDepositLinkCount);
    Assert.Equal(2, appt.DepositLinkAttempts);
  }

  [Fact]
  public void Regenerate_WhilePreviousLinkStillValid_ShouldNotCountAsExpired()
  {
    var appt = BookedAppointment();
    var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", now.AddHours(24), now);

    // Personel nadpisuje wciąż ważny link (np. koryguje kwotę) — to nie jest nieudana próba.
    appt.GenerateDepositLink(new Money(40m, "PLN"), "cs_2", "https://pay/y", now.AddHours(25), now.AddHours(1));

    Assert.Equal(0, appt.ExpiredDepositLinkCount);
    Assert.Equal(2, appt.DepositLinkAttempts);
  }

  [Fact]
  public void ExpiredDepositLinkCount_ShouldAccumulateAcrossFailedAttempts()
  {
    var appt = BookedAppointment();
    var t = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    for (var i = 0; i < 3; i++)
    {
      appt.GenerateDepositLink(new Money(30m, "PLN"), $"cs_{i}", $"https://pay/{i}", t.AddHours(24), t);
      t = t.AddHours(30); // każdy link zdąża wygasnąć przed kolejną próbą
    }

    // 3 wygenerowane linki, z czego 2 poprzednie wygasły bez opłaty.
    Assert.Equal(3, appt.DepositLinkAttempts);
    Assert.Equal(2, appt.ExpiredDepositLinkCount);
  }

  [Fact]
  public void GenerateDepositLink_AfterPaid_ShouldNotBumpCounters()
  {
    var appt = BookedAppointment();
    var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", now.AddHours(24), now);
    appt.MarkDepositPaid(now.AddHours(1));

    Assert.Throws<AppointmentBookingRuleException>(() =>
      appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_2", "https://pay/y", now.AddHours(48), now.AddHours(30)));

    Assert.Equal(1, appt.DepositLinkAttempts);
    Assert.Equal(0, appt.ExpiredDepositLinkCount);
  }

  [Fact]
  public void IsDepositLinkExpired_ShouldReflectExpiryWhileAwaiting()
  {
    var appt = BookedAppointment();
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_1", "https://pay/x", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow);

    Assert.True(appt.IsDepositLinkExpired(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)));
    Assert.False(appt.IsDepositLinkExpired(new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc)));

    appt.MarkDepositPaid(DateTime.UtcNow);
    // Po opłaceniu nie jest „wygasły", niezależnie od czasu.
    Assert.False(appt.IsDepositLinkExpired(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)));
  }
}
