using App.Application.Booking;
using App.Application.Booking.SelfService;
using App.Application.Booking.SelfService.Commands;
using App.Application.Booking.SelfService.Queries;
using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.UnitTests.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using App.Application.UnitTests.TestSupport;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// APP-SS-* — testy handlerów Self-Service (Request/Verify OTP, Cancel, Reschedule, Get).
/// </summary>
public sealed class SelfServiceHandlerTests
{
  // ── SS-001 / SS-002: RequestSelfServiceOtp ─────────────────────────────────────────────

  [Fact]
  public async Task RequestOtp_with_email_creates_otp_and_sends_email()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var mail = new CapturingEmailSender();
    var handler = new RequestSelfServiceOtpCommandHandler(db, mail, new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, "Guest@Example.com", null), ct);

    var otp = await db.SelfServiceOtps.AsNoTracking().FirstAsync(ct);
    Assert.Equal("guest@example.com", otp.Email);
    Assert.Null(otp.PhoneE164);
    Assert.False(string.IsNullOrEmpty(otp.CodeHash));
    Assert.Single(mail.Sent);
    Assert.Equal("guest@example.com", mail.Sent[0].ToEmail);
  }

  [Fact]
  public async Task RequestOtp_with_phone_creates_otp_with_e164_phone_and_no_email_sent()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var mail = new CapturingEmailSender();
    var handler = new RequestSelfServiceOtpCommandHandler(db, mail, new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, null, "+48501111222"), ct);

    var otp = await db.SelfServiceOtps.AsNoTracking().FirstAsync(ct);
    Assert.Equal("+48501111222", otp.PhoneE164);
    Assert.Null(otp.Email);
    Assert.Empty(mail.Sent);
  }

  [Fact]
  public async Task RequestOtp_second_request_within_60s_throws_RateLimit()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var mail = new CapturingEmailSender();
    var handler = new RequestSelfServiceOtpCommandHandler(db, mail, new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, "x@y.co", null), ct);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, "x@y.co", null), ct));
    Assert.Equal(ErrorCodes.RateLimitExceeded, ex.ErrorCode);
  }

  [Theory]
  [InlineData("a@b.co", "+48501111222")] // both provided
  [InlineData(null, null)]                // neither provided
  public async Task RequestOtp_with_invalid_contact_combination_throws_missing_contact(string? email, string? phone)
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var handler = new RequestSelfServiceOtpCommandHandler(db, new CapturingEmailSender(), new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, email, phone), ct));
    Assert.Equal(ErrorCodes.AppointmentOtpMissingContact, ex.ErrorCode);
  }

  [Fact]
  public async Task RequestOtp_with_unknown_slug_throws_NotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, _) = await SetupAsync(ct);
    var handler = new RequestSelfServiceOtpCommandHandler(db, new CapturingEmailSender(), new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand("unknown-slug", "a@b.co", null), ct));
  }

  // ── SS-PHONE: RequestSelfServiceOtp phone channel (SMS + anti-abuse) ───────────────────

  [Fact]
  public async Task RequestOtp_with_phone_sends_sms_and_records_usage()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var sms = new CapturingPhoneOtpSender();
    var usage = new CapturingUsageRecorder();
    var handler = new RequestSelfServiceOtpCommandHandler(
      db, new CapturingEmailSender(), sms, new NoOpOtpProtection(), usage, new FakeSmsUsageGuard(withinCap: true), db, TimeProvider.System);

    await handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, null, "+48501111222"), ct);

    var sent = Assert.Single(sms.Sent);
    Assert.Equal("+48501111222", sent.PhoneE164);
    Assert.Matches(@"^\d{6}$", sent.Code);
    var recorded = Assert.Single(usage.Entries);
    Assert.Equal("Sms", recorded.Channel);
    Assert.Equal(NotificationType.CustomerVerificationOtp, recorded.Type);
    Assert.True(recorded.Success);
  }

  [Fact]
  public async Task RequestOtp_with_non_polish_phone_is_rejected_before_sending()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var sms = new CapturingPhoneOtpSender();
    var handler = new RequestSelfServiceOtpCommandHandler(
      db, new CapturingEmailSender(), sms, new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, null, "+447911123456"), ct));

    Assert.Equal(ErrorCodes.AppointmentOtpUnsupportedPhoneRegion, ex.ErrorCode);
    Assert.Empty(sms.Sent);
    Assert.Empty(await db.SelfServiceOtps.AsNoTracking().ToListAsync(ct));
  }

  [Fact]
  public async Task RequestOtp_with_phone_per_number_cap_exceeded_is_rejected_before_sending()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var sms = new CapturingPhoneOtpSender();
    var handler = new RequestSelfServiceOtpCommandHandler(
      db, new CapturingEmailSender(), sms, new PhoneCapReachedOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await Assert.ThrowsAsync<RateLimitExceededException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, null, "+48501111222"), ct));

    Assert.Empty(sms.Sent);
  }

  [Fact]
  public async Task RequestOtp_with_phone_monthly_cap_exceeded_throws_and_does_not_send()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var sms = new CapturingPhoneOtpSender();
    var usage = new CapturingUsageRecorder();
    var handler = new RequestSelfServiceOtpCommandHandler(
      db, new CapturingEmailSender(), sms, new NoOpOtpProtection(), usage, new FakeSmsUsageGuard(withinCap: false), db, TimeProvider.System);

    await Assert.ThrowsAsync<SmsServiceUnavailableException>(() =>
      handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, null, "+48501111222"), ct));

    Assert.Empty(sms.Sent);
    Assert.Empty(usage.Entries);
  }

  // ── SS-004 / SS-005 / SS-006: VerifySelfServiceOtp ─────────────────────────────────────

  [Fact]
  public async Task VerifyOtp_with_correct_code_returns_session_token_and_expiry()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var code = "424242";
    db.SelfServiceOtps.Add(SelfServiceOtp.ForEmail(tenant.Id, "u@v.co", SelfServiceCodeHasher.Hash(code), DateTime.UtcNow, TimeSpan.FromMinutes(10)));
    await db.SaveChangesAsync(ct);

    var handler = new VerifySelfServiceOtpCommandHandler(db, db, TimeProvider.System);
    var result = await handler.Handle(new VerifySelfServiceOtpCommand(tenant.Slug, "u@v.co", null, code), ct);

    Assert.NotEqual(Guid.Empty, result.SessionToken);
    // Sesja żyje 2h (SelfServiceSessionPolicy.SessionLifetime) — dolna granica z zapasem na drift zegara.
    Assert.True(result.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(115));
  }

  [Fact]
  public async Task VerifyOtp_when_no_active_otp_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var handler = new VerifySelfServiceOtpCommandHandler(db, db, TimeProvider.System);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new VerifySelfServiceOtpCommand(tenant.Slug, "nobody@nope.co", null, "111111"), ct));
    Assert.Equal(ErrorCodes.AppointmentOtpVerificationRequired, ex.ErrorCode);
  }

  [Fact]
  public async Task VerifyOtp_after_three_bad_attempts_throws_TooManyFailures()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    db.SelfServiceOtps.Add(SelfServiceOtp.ForEmail(tenant.Id, "u@v.co", SelfServiceCodeHasher.Hash("111111"), DateTime.UtcNow, TimeSpan.FromMinutes(10)));
    await db.SaveChangesAsync(ct);

    var handler = new VerifySelfServiceOtpCommandHandler(db, db, TimeProvider.System);
    for (var i = 0; i < 2; i++)
    {
      await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(new VerifySelfServiceOtpCommand(tenant.Slug, "u@v.co", null, "999999"), ct));
    }

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new VerifySelfServiceOtpCommand(tenant.Slug, "u@v.co", null, "999999"), ct));
    Assert.Equal(ErrorCodes.AppointmentOtpTooManyFailures, ex.ErrorCode);
  }

  // ── SS-008: Session resolver via Cancel handler ────────────────────────────────────────

  [Fact]
  public async Task Cancel_with_unknown_session_token_throws_ForbiddenAccess()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var handler = BuildCancelHandler(db);

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, Guid.NewGuid(), Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Cancel_with_expired_session_throws_ForbiddenAccess()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);

    var nowMinusHour = DateTime.UtcNow.AddHours(-1);
    var otp = SelfServiceOtp.ForEmail(tenant.Id, "u@v.co", SelfServiceCodeHasher.Hash("111111"), nowMinusHour, TimeSpan.FromMinutes(45));
    otp.TryConsume(SelfServiceCodeHasher.Hash("111111"), nowMinusHour, TimeSpan.FromMinutes(30));
    db.SelfServiceOtps.Add(otp);
    await db.SaveChangesAsync(ct);

    var handler = BuildCancelHandler(db);

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, Guid.NewGuid()), ct));
  }

  // ── SS-011 / SS-012 / SS-013: Cancel ───────────────────────────────────────────────────

  [Fact]
  public async Task Cancel_sets_appointment_status_to_canceled_and_records_self_service_change()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = BuildCancelHandler(db);
    await handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appt.Id }, ct);
    Assert.Equal(AppointmentStatus.Canceled, reloaded!.Status);
    Assert.Equal(1, reloaded.SelfServiceChangeKind);
  }

  [Fact]
  public async Task Cancel_throws_when_appointment_date_in_past()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      pastDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = BuildCancelHandler(db);
    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct));
    Assert.Equal(ErrorCodes.InvalidArgument, ex.ErrorCode);
  }

  [Fact]
  public async Task Cancel_throws_Forbidden_when_appointment_has_no_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (_, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, "session@e.co");
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, null,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = BuildCancelHandler(db);
    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct));
  }

  [Fact]
  public async Task Cancel_throws_Forbidden_when_customer_email_does_not_match_session()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    // session email = other@e.co, customer.Email = customer@e.co
    var otp = SeedActiveSession(db, tenant, "other@e.co");
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = BuildCancelHandler(db);
    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct));
  }

  [Fact]
  public async Task Cancel_throws_NotFound_when_appointment_id_does_not_exist_for_session_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var otp = SeedActiveSession(db, tenant, "u@v.co");

    var handler = BuildCancelHandler(db);
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Cancel_completes_successfully_when_notifier_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = new CancelSelfServiceAppointmentCommandHandler(
      db, db, new ThrowingPublisher(), NullLogger<CancelSelfServiceAppointmentCommandHandler>.Instance, TimeProvider.System, new RecordingInspirationCleanup());

    await handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct);

    var reloaded = await db.Appointments.FindAsync(new object[] { appt.Id }, ct);
    Assert.Equal(AppointmentStatus.Canceled, reloaded!.Status);
  }

  [Fact]
  public async Task Cancel_purges_inspiration_images_from_db_and_storage()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    appt.AddInspirationImage(new AppointmentInspirationLine(
      "https://cdn/a.webp", "https://cdn/a_thumb.webp", "inspirations/a.webp", "inspirations/a_thumb.webp"));
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var cleanup = new RecordingInspirationCleanup();
    var handler = new CancelSelfServiceAppointmentCommandHandler(
      db, db, new CapturingPublisher(), NullLogger<CancelSelfServiceAppointmentCommandHandler>.Instance, TimeProvider.System, cleanup);

    await handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct);

    // Anulowanie czyści zdjęcia z bazy ORAZ zleca skasowanie obu obiektów (główny + miniatura) z R2.
    var reloaded = await db.Appointments
      .IgnoreQueryFilters()
      .Include(a => a.InspirationImages)
      .FirstAsync(a => a.Id == appt.Id, ct);
    Assert.Equal(AppointmentStatus.Canceled, reloaded.Status);
    Assert.Empty(reloaded.InspirationImages);
    Assert.Contains("inspirations/a.webp", cleanup.Purged);
    Assert.Contains("inspirations/a_thumb.webp", cleanup.Purged);
  }

  // ── SS-009 / SS-020: GetSelfServiceAppointments ────────────────────────────────────────

  [Fact]
  public async Task Get_returns_appointments_matched_by_email()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      today.AddDays(2), new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    await db.SaveChangesAsync(ct);

    var handler = new GetSelfServiceAppointmentsQueryHandler(db, TimeProvider.System);
    var result = await handler.Handle(new GetSelfServiceAppointmentsQuery(tenant.Slug, otp.SessionToken!.Value), ct);

    Assert.Single(result);
    Assert.True(result[0].CanCancel);
    Assert.True(result[0].CanReschedule);
  }

  [Fact]
  public async Task Get_returns_full_combo_service_ids_in_position_order()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    // Druga usługa w combo (dodatek).
    var addon = new Service(tenant.Id, service.CategoryId, service.VatRateId, "Henna", new Money(40m, "PLN"), 20);
    db.Services.Add(addon);
    await db.SaveChangesAsync(ct);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    // Combo: usługa główna + dodatek (pozycja 0 = primary, 1 = addon).
    db.Appointments.Add(new Appointment(
      tenant.Id, employee.Id, customer.Id, today.AddDays(2), new TimeOnly(10, 0),
      AppointmentStatus.Booked, string.Empty, null,
      new[]
      {
        new AppointmentServiceLine(service.Id, 30, new Money(80m, "PLN")),
        new AppointmentServiceLine(addon.Id, 20, new Money(40m, "PLN")),
      }));
    await db.SaveChangesAsync(ct);

    var handler = new GetSelfServiceAppointmentsQueryHandler(db, TimeProvider.System);
    var result = await handler.Handle(new GetSelfServiceAppointmentsQuery(tenant.Slug, otp.SessionToken!.Value), ct);

    Assert.Single(result);
    Assert.Equal(service.Id, result[0].ServiceId);
    // Sedno fixu combo: dostępność przy przekładaniu liczona dla CAŁEGO combo, więc DTO niesie
    // wszystkie usługi wg Position (primary jako pierwsza).
    Assert.Equal(new[] { service.Id, addon.Id }, result[0].ServiceIds);
  }

  [Fact]
  public async Task Get_returns_empty_when_no_matching_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var otp = SeedActiveSession(db, tenant, "noone@unknown.co");

    var handler = new GetSelfServiceAppointmentsQueryHandler(db, TimeProvider.System);
    var result = await handler.Handle(new GetSelfServiceAppointmentsQuery(tenant.Slug, otp.SessionToken!.Value), ct);

    Assert.Empty(result);
  }

  // ── SS-010: cancelable/reschedulable flags ─────────────────────────────────────────────

  [Fact]
  public async Task Get_marks_completed_appointment_as_not_cancelable()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);
    var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      future, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    appt.ChangeStatus(AppointmentStatus.Completed);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var handler = new GetSelfServiceAppointmentsQueryHandler(db, TimeProvider.System);
    var result = await handler.Handle(new GetSelfServiceAppointmentsQuery(tenant.Slug, otp.SessionToken!.Value), ct);

    Assert.Single(result);
    Assert.False(result[0].CanCancel);
    Assert.False(result[0].CanReschedule);
  }

  // ── M12: twardy sufit liczby zwracanych wizyt (Take) ───────────────────────────────────

  [Fact]
  public async Task Get_caps_returned_appointments_at_max_items()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var (customer, employee, service) = SeedCustomerEmployeeService(db, tenant);
    var otp = SeedActiveSession(db, tenant, customer.Email);

    // 250 przyszłych wizyt (> sufit 200) — handler musi zwrócić dokładnie 200 najnowszych.
    var baseDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    for (var i = 0; i < 250; i++)
    {
      var date = baseDate.AddDays(i);
      db.Appointments.Add(new Appointment(
        tenant.Id, employee.Id, service.Id, customer.Id,
        date, new TimeOnly(10, 0), new TimeOnly(10, 30),
        AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null));
    }
    await db.SaveChangesAsync(ct);

    var handler = new GetSelfServiceAppointmentsQueryHandler(db, TimeProvider.System);
    var result = await handler.Handle(new GetSelfServiceAppointmentsQuery(tenant.Slug, otp.SessionToken!.Value), ct);

    Assert.Equal(200, result.Count);
    // Sortowanie malejące po dacie → najnowsza wizyta (baseDate + 249) musi być na liście.
    Assert.Contains(result, r => r.Date == baseDate.AddDays(249));
    // Najstarsze wizyty (poza oknem 200) zostają odcięte.
    Assert.DoesNotContain(result, r => r.Date == baseDate);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static async Task<(ApplicationDbContext db, Tenant tenant)> SetupAsync(CancellationToken ct)
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    var tenant = new Tenant("SS Salon", "ss-salon-" + Guid.NewGuid().ToString("N")[..6]);
    typeof(Entity).GetProperty("Id")!.SetValue(tenant, tenantId);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    return (db, tenant);
  }

  private static (Customer customer, Employee employee, Service service) SeedCustomerEmployeeService(
    ApplicationDbContext db,
    Tenant tenant)
  {
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Ann", "Smith", "ann@salon.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Cut", new Money(80m, "PLN"), 30);
    var customer = new Customer(tenant.Id, "Jan", "Kowalski", "customer@e.co", new PhoneNumber("+48501234567"), "");
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Customers.Add(customer);
    db.SaveChanges();
    return (customer, employee, service);
  }

  private static SelfServiceOtp SeedActiveSession(ApplicationDbContext db, Tenant tenant, string email)
  {
    var nowUtc = DateTime.UtcNow;
    var code = "123456";
    var otp = SelfServiceOtp.ForEmail(tenant.Id, email, SelfServiceCodeHasher.Hash(code), nowUtc, TimeSpan.FromMinutes(10));
    otp.TryConsume(SelfServiceCodeHasher.Hash(code), nowUtc, TimeSpan.FromMinutes(30));
    db.SelfServiceOtps.Add(otp);
    db.SaveChanges();
    return otp;
  }

  private static CancelSelfServiceAppointmentCommandHandler BuildCancelHandler(ApplicationDbContext db)
    => new(db, db, new CapturingPublisher(), NullLogger<CancelSelfServiceAppointmentCommandHandler>.Instance, TimeProvider.System, new RecordingInspirationCleanup());

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class CapturingEmailSender : IBookingOtpEmailSender
  {
    public List<(string ToEmail, string Code, string SalonName)> Sent { get; } = new();
    public Task SendOtpCodeAsync(string toEmail, string sixDigitCode, EmailBrand brand, CancellationToken cancellationToken = default)
    {
      Sent.Add((toEmail, sixDigitCode, brand.Name));
      return Task.CompletedTask;
    }
  }

  private sealed class CapturingPhoneOtpSender : IPhoneOtpSender
  {
    public List<(string PhoneE164, string Code)> Sent { get; } = new();
    public Task SendOtpAsync(string phoneE164, string code, CancellationToken ct = default)
    {
      Sent.Add((phoneE164, code));
      return Task.CompletedTask;
    }
  }

  /// <summary>Test-only no-op — pomija anti-bomb throttle/cooldown. Faktyczna logika throttle
  /// jest weryfikowana w integration testach (BookingOtpEmailBombProtectionIntegrationTests).</summary>
  private sealed class NoOpOtpProtection : IBookingOtpProtection
  {
    public void AssertCanRequestOtp(Guid appointmentId, string? clientIp) { }
    public void RegisterOtpRequestSucceeded(Guid appointmentId, string? clientIp) { }
    public void AssertCanSendOtpToEmail(string email) { }
    public void RegisterOtpSentToEmail(string email) { }
    public void AssertCanSendOtpToPhone(string phoneE164) { }
    public void RegisterOtpSentToPhone(string phoneE164) { }
    public void AssertCanSendOtpSmsFromIp(string? clientIp) { }
    public void RegisterOtpSmsSentFromIp(string? clientIp) { }

    public void AssertCanSendOtpEmailFromIp(string? clientIp) { }

    public void RegisterOtpEmailSentFromIp(string? clientIp) { }

    public void AssertCanConfirmBookingFromIp(string? clientIp) { }
    public void RegisterHoldCreatedForIp(string? clientIp) { }
    public void ReleaseHoldForIp(string? clientIp) { }
    public void RecordVerifyOtpAttempt(string? clientIp) { }
    public bool IsVerificationBlocked(Guid appointmentId) => false;
    public int RegisterFailedVerificationAttempt(Guid appointmentId) => 0;
    public void ClearVerificationAttempts(Guid appointmentId) { }
    public bool IsTargetVerificationBlocked(string target) => false;
    public int RegisterFailedVerificationForTarget(string target) => 0;
    public void ClearTargetVerificationAttempts(string target) { }
    public void AssertCanConfirmWithSession(Guid sessionToken, string? clientIp) { }
    public void AssertCanReschedule(Guid sessionToken, Guid appointmentId, string? clientIp) { }
  }

  /// <summary>Symuluje wyczerpany per-numer cap — AssertCanSendOtpToPhone rzuca jak realny serwis.</summary>
  private sealed class PhoneCapReachedOtpProtection : IBookingOtpProtection
  {
    public void AssertCanRequestOtp(Guid appointmentId, string? clientIp) { }
    public void RegisterOtpRequestSucceeded(Guid appointmentId, string? clientIp) { }
    public void AssertCanSendOtpToEmail(string email) { }
    public void RegisterOtpSentToEmail(string email) { }
    public void AssertCanSendOtpToPhone(string phoneE164) =>
      throw new RateLimitExceededException(
        "Wysłaliśmy już maksymalną liczbę kodów na ten numer telefonu. Spróbuj ponownie za godzinę.",
        retryAfterSeconds: 3600);
    public void RegisterOtpSentToPhone(string phoneE164) { }
    public void AssertCanSendOtpSmsFromIp(string? clientIp) { }
    public void RegisterOtpSmsSentFromIp(string? clientIp) { }

    public void AssertCanSendOtpEmailFromIp(string? clientIp) { }

    public void RegisterOtpEmailSentFromIp(string? clientIp) { }

    public void AssertCanConfirmBookingFromIp(string? clientIp) { }
    public void RegisterHoldCreatedForIp(string? clientIp) { }
    public void ReleaseHoldForIp(string? clientIp) { }
    public void RecordVerifyOtpAttempt(string? clientIp) { }
    public bool IsVerificationBlocked(Guid appointmentId) => false;
    public int RegisterFailedVerificationAttempt(Guid appointmentId) => 0;
    public void ClearVerificationAttempts(Guid appointmentId) { }
    public bool IsTargetVerificationBlocked(string target) => false;
    public int RegisterFailedVerificationForTarget(string target) => 0;
    public void ClearTargetVerificationAttempts(string target) { }
    public void AssertCanConfirmWithSession(Guid sessionToken, string? clientIp) { }
    public void AssertCanReschedule(Guid sessionToken, Guid appointmentId, string? clientIp) { }
  }

}
