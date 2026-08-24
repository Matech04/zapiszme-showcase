using App.Application.Booking;
using App.Application.Booking.SelfService;
using App.Application.Booking.SelfService.Commands;
using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Notifications.Events;
using App.Application.UnitTests.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using App.Application.UnitTests.TestSupport;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// NOTIF-005..008, 010 — testy weryfikujące wywołanie notifiera i graceful-degradation.
/// </summary>
public sealed class SelfServiceNotificationTests
{
  // NOTIF-005: RequestSelfServiceOtp email channel = jeden wysłany email z 6-cyfrowym kodem
  [Fact]
  public async Task RequestOtp_sends_email_exactly_once_with_six_digit_code()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant) = await SetupAsync(ct);
    var mail = new CapturingEmailSender();
    var handler = new RequestSelfServiceOtpCommandHandler(
      db, mail, new CapturingPhoneOtpSender(), new NoOpOtpProtection(), new CapturingUsageRecorder(), new FakeSmsUsageGuard(), db, TimeProvider.System);

    await handler.Handle(new RequestSelfServiceOtpCommand(tenant.Slug, "guest@e.co", null), ct);

    Assert.Single(mail.Sent);
    Assert.Matches(@"^\d{6}$", mail.Sent[0].Code);
  }

  // NOTIF-006: Cancel publishes AppointmentCancelledEvent with correct ids
  [Fact]
  public async Task Cancel_self_service_publishes_event_with_correct_ids()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, customer, employee, service) = await SetupWithCustomerAsync(ct);
    var otp = SeedActiveSession(db, tenant, customer.Email);

    var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      futureDate, new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);
    db.Appointments.Add(appt);
    await db.SaveChangesAsync(ct);

    var publisher = new CapturingPublisher();
    var handler = new CancelSelfServiceAppointmentCommandHandler(
      db, db, publisher, NullLogger<CancelSelfServiceAppointmentCommandHandler>.Instance, TimeProvider.System, new RecordingInspirationCleanup());

    await handler.Handle(new CancelSelfServiceAppointmentCommand(tenant.Slug, otp.SessionToken!.Value, appt.Id), ct);

    var published = Assert.Single(publisher.Published);
    var evt = Assert.IsType<AppointmentCancelledEvent>(published);
    Assert.Equal(tenant.Id, evt.TenantId);
    Assert.Equal(appt.Id, evt.AppointmentId);
  }

  // NOTIF-008: Cancel completes successfully when publish throws (best-effort)
  [Fact]
  public async Task Cancel_self_service_completes_when_publish_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenant, customer, employee, service) = await SetupWithCustomerAsync(ct);
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

  private static async Task<(ApplicationDbContext db, Tenant tenant, Customer customer, Employee employee, Service service)> SetupWithCustomerAsync(CancellationToken ct)
  {
    var (db, tenant) = await SetupAsync(ct);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, null, "Ann", "Smith", "ann@notif.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Cut", new Money(80m, "PLN"), 30);
    var customer = new Customer(tenant.Id, "Jan", "K", "customer@notif.local", new PhoneNumber("+48501234567"), "");
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);
    return (db, tenant, customer, employee, service);
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

  private sealed class NoOpOtpProtection : App.Application.Common.Interfaces.IBookingOtpProtection
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

}
