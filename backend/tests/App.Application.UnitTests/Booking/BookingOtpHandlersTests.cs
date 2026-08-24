using App.Application.Booking;
using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Common.Email;
using App.Application.Common.Interfaces;
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
using App.Infrastructure.Booking;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Booking;

public sealed class BookingOtpHandlersTests
{
  private sealed class TestCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; set; }
  }

  /// <summary>Trywialny token grantu uploadu inspiracji — testy OTP nie sprawdzają jego wartości.</summary>
  private sealed class FakeInspirationUploadTokenService : IInspirationUploadTokenService
  {
    public static readonly FakeInspirationUploadTokenService Instance = new();

    public string Issue(Guid appointmentId) => appointmentId.ToString("N");

    public bool TryValidate(string token, out Guid appointmentId)
        => Guid.TryParseExact(token, "N", out appointmentId);
  }

  private sealed class CapturingBookingOtpEmailSender : IBookingOtpEmailSender
  {
    public List<(string ToEmail, string Code, string SalonName)> Sent { get; } = new();

    public Task SendOtpCodeAsync(
        string toEmail,
        string sixDigitCode,
        EmailBrand brand,
        CancellationToken cancellationToken = default)
    {
      Sent.Add((toEmail, sixDigitCode, brand.Name));
      return Task.CompletedTask;
    }
  }

  private sealed class CapturingPhoneOtpSender : App.Application.Common.Security.IPhoneOtpSender
  {
    public List<(string PhoneE164, string Code)> Sent { get; } = new();

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken = default)
    {
      Sent.Add((phoneE164, code));
      return Task.CompletedTask;
    }
  }

  private sealed class Fixture : IDisposable
  {
    public ApplicationDbContext Context { get; }
    public AppointmentRepository AppointmentRepository { get; }
    public CustomerRepository CustomerRepository { get; }
    public TestCurrentTenantService TenantService { get; }
    public Appointment Appointment { get; }
    public Guid LeaseToken { get; }
    public Tenant Tenant { get; }

    public TenantRepository TenantRepository { get; }

    public Fixture(CustomerVerificationChannel verificationChannel, bool requireCustomerName = false)
    {
      TenantService = new TestCurrentTenantService();
      var options = new DbContextOptionsBuilder<ApplicationDbContext>()
          .UseInMemoryDatabase(Guid.NewGuid().ToString())
          .Options;

      Context = new ApplicationDbContext(options, TenantService);
      Tenant = new Tenant("Salon OTP", "salon-otp");
      Tenant.Update(Tenant.Name, Tenant.Slug, verificationChannel, requireCustomerName: requireCustomerName);

      var category = new ServiceCategory(Tenant.Id, "Default", 0);
      var vat = new VatRate(Tenant.Id, "VAT", 0.23m);
      var employee = new Employee(Tenant.Id, userId: null, "Ann", "Smith", "ann@salon.local");
      var service = new Service(Tenant.Id, category.Id, vat.Id, "Cut", new Money(80m, "PLN"), 30);

      Context.Tenants.Add(Tenant);
      Context.ServiceCategories.Add(category);
      Context.VatRates.Add(vat);
      Context.Employees.Add(employee);
      Context.Services.Add(service);

      TenantService.TenantId = Tenant.Id;

      LeaseToken = Guid.NewGuid();
      var lease = new HoldLease(LeaseToken, DateTime.UtcNow.AddHours(2));
      Appointment = new Appointment(
          Tenant.Id,
          employee.Id,
          service.Id,
          customerId: null,
          new DateOnly(2026, 6, 1),
          new TimeOnly(10, 0),
          new TimeOnly(11, 0),
          AppointmentStatus.AwaitingOtp,
          new Money(80m, "PLN"),
          appointmentNotes: string.Empty,
          lease);

      Context.Appointments.Add(Appointment);
      Context.SaveChanges();

      AppointmentRepository = new AppointmentRepository(Context);
      CustomerRepository = new CustomerRepository(Context);
      TenantRepository = new TenantRepository(Context);
    }

    public void Dispose() => Context.Dispose();
  }

  private static IHttpContextAccessor HttpAccessor()
  {
    return new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
  }

  [Fact]
  public async Task RequestOtp_with_email_channel_sends_email_and_persists_otp()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var cache = new MemoryCache(new MemoryCacheOptions());
    var protection = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var mail = new CapturingBookingOtpEmailSender();
    var phone = new CapturingPhoneOtpSender();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        mail,
        phone,
        protection,
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: null, Email: "Guest@Example.com"),
        ct);

    Assert.Empty(phone.Sent);
    Assert.Single(mail.Sent);
    Assert.Equal("guest@example.com", mail.Sent[0].ToEmail);
    Assert.Equal(fx.Tenant.Name, mail.Sent[0].SalonName);
    Assert.Matches(@"^\d{6}$", mail.Sent[0].Code);

    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.NotNull(reloaded!.OtpVerification);
    Assert.Equal(OtpVerificationChannel.Email, reloaded.OtpVerification.Channel);
    Assert.True(reloaded.OtpVerification.IsValid(mail.Sent[0].Code, DateTime.UtcNow));
  }

  [Fact]
  public async Task RequestOtp_with_phone_channel_sends_sms_and_persists_otp()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone);
    var cache = new MemoryCache(new MemoryCacheOptions());
    var protection = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var mail = new CapturingBookingOtpEmailSender();
    var phone = new CapturingPhoneOtpSender();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        mail,
        phone,
        protection,
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: "+48501111222", Email: null),
        ct);

    Assert.Empty(mail.Sent);
    Assert.Single(phone.Sent);
    Assert.Equal("+48501111222", phone.Sent[0].PhoneE164);
    Assert.Matches(@"^\d{6}$", phone.Sent[0].Code);

    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.NotNull(reloaded!.OtpVerification);
    Assert.Equal(OtpVerificationChannel.Phone, reloaded.OtpVerification.Channel);
    Assert.Equal("+48501111222", reloaded.OtpVerification.PhoneE164);
    // Kod wysłany SMS-em musi weryfikować się względem zapisanego hasha.
    Assert.True(reloaded.OtpVerification.IsValid(phone.Sent[0].Code, DateTime.UtcNow));
  }

  [Fact]
  public async Task RequestOtp_with_phone_channel_records_sms_usage_for_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone);
    var usage = new CapturingUsageRecorder();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        new CapturingPhoneOtpSender(),
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        usage,
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: "+48501111222", Email: null),
        ct);

    var entry = Assert.Single(usage.Entries);
    Assert.Equal("Sms", entry.Channel);
    Assert.Equal(NotificationType.CustomerVerificationOtp, entry.Type);
    Assert.True(entry.Success);
  }

  [Fact]
  public async Task RequestOtp_with_email_channel_records_email_usage_for_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var usage = new CapturingUsageRecorder();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        new CapturingPhoneOtpSender(),
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        usage,
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: null, Email: "guest@example.com"),
        ct);

    var entry = Assert.Single(usage.Entries);
    Assert.Equal("Email", entry.Channel);
    Assert.Equal(NotificationType.CustomerVerificationOtp, entry.Type);
    Assert.True(entry.Success);
  }

  [Fact]
  public async Task RequestOtp_phone_over_sms_cap_throws_and_does_not_send()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone);
    var phone = new CapturingPhoneOtpSender();
    var usage = new CapturingUsageRecorder();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        phone,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        usage,
        new FakeSmsUsageGuard(withinCap: false), // limit przekroczony
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await Assert.ThrowsAsync<SmsServiceUnavailableException>(() =>
        handler.Handle(
            new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: "+48501111222", Email: null),
            ct));

    // OTP nie został wysłany ani zarejestrowany do zużycia.
    Assert.Empty(phone.Sent);
    Assert.Empty(usage.Entries);
  }

  [Fact]
  public async Task RequestOtp_with_wrong_lease_token_throws_ForbiddenAccessException()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        new CapturingPhoneOtpSender(),
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        handler.Handle(
            new RequestOtpCommand(Guid.NewGuid(), fx.Appointment.Id, null, "a@b.co"),
            ct));
  }

  [Fact]
  public async Task RequestOtp_when_email_missing_for_email_channel_throws_AppointmentBookingRuleException()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        new CapturingPhoneOtpSender(),
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, null, Email: null),
            ct));
  }

  [Fact]
  public async Task RequestOtp_second_immediate_request_throws_RateLimitExceededException()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var cache = new MemoryCache(new MemoryCacheOptions());
    var protection = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        new CapturingPhoneOtpSender(),
        protection,
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, null, "one@test.local"),
        ct);

    await Assert.ThrowsAsync<RateLimitExceededException>(() =>
        handler.Handle(
            new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, null, "two@test.local"),
            ct));
  }

  // APPT-008 Negative: kod był poprawny treściowo, ale OTP wygasł (ExpiryTimeUtc w przeszłości)
  // → handler musi rzucić AppointmentBookingRuleException z ErrorCodes.AppointmentOtpInvalidCode.
  [Fact]
  public async Task VerifyOtp_with_expired_otp_throws_AppointmentBookingRuleException_with_invalid_code()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(-1)));
    await fx.Context.SaveChangesAsync(ct);

    var cache = new MemoryCache(new MemoryCacheOptions());
    var protection = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        protection,
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242"),
            ct));

    Assert.Equal(ErrorCodes.AppointmentOtpInvalidCode, ex.ErrorCode);
  }

  [Fact]
  public async Task VerifyOtp_with_valid_code_sets_booked()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var cache = new MemoryCache(new MemoryCacheOptions());
    var protection = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        protection,
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var result = await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242"),
        ct);

    // Tryb automatyczny → front pokazuje „Wizyta potwierdzona".
    Assert.False(result.RequiresManualConfirmation);
    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(AppointmentStatus.Booked, reloaded!.Status);
    // Po potwierdzeniu dzierżawa MUSI być wyczyszczona — inaczej job cyklu życia
    // mógłby usunąć potwierdzoną wizytę po wygaśnięciu starej dzierżawy.
    Assert.Null(reloaded.Lease);
  }

  // Regresja: tryb ręcznego potwierdzania → wizyta przechodzi w Pending. Dzierżawa MUSI być
  // wyczyszczona, w przeciwnym razie AppointmentStatusLifecycleHostedService kasuje z bazy
  // potwierdzoną (Pending) wizytę po wygaśnięciu dzierżawy OTP — salon dostawał powiadomienie,
  // ale wizyta znikała kilka minut później.
  [Fact]
  public async Task VerifyOtp_in_manual_mode_sets_pending_and_clears_hold_lease()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Tenant.Update(
        fx.Tenant.Name,
        fx.Tenant.Slug,
        appointmentConfirmationMode: AppointmentConfirmationMode.Manual);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var result = await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242"),
        ct);

    // Tryb ręczny → front pokazuje „rezerwacja przyjęta, czeka na potwierdzenie salonu".
    Assert.True(result.RequiresManualConfirmation);
    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(AppointmentStatus.Pending, reloaded!.Status);
    Assert.Null(reloaded.Lease);
  }

  [Fact]
  public async Task VerifyOtp_without_prior_otp_throws_AppointmentBookingRuleException()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "000000"),
            ct));
  }

  [Fact]
  public async Task VerifyOtp_wrong_code_first_attempt_message_contains_remaining()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "111111", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "222222"),
            ct));

    Assert.Contains("Pozostało prób: 2", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task VerifyOtp_third_wrong_attempt_throws_block_message()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "111111", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    _ = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "bad1"), ct));
    _ = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "bad2"), ct));

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "bad3"), ct));

    Assert.Contains("3 razy podano", ex.Message, StringComparison.Ordinal);
  }

  // Salon wymaga imienia i nazwiska — brak danych musi zatrzymać flow PRZED wysłaniem OTP
  // (żeby nie palić kredytów SMS / nie wysyłać maila na rezerwację, która i tak nie przejdzie).
  [Fact]
  public async Task RequestOtp_when_name_required_but_missing_throws_and_does_not_send()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone, requireCustomerName: true);
    var phone = new CapturingPhoneOtpSender();
    var usage = new CapturingUsageRecorder();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        phone,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        usage,
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: "+48501111222", Email: null, FirstName: "Anna", LastName: "  "),
            ct));

    Assert.Equal(ErrorCodes.AppointmentOtpMissingName, ex.ErrorCode);
    Assert.Empty(phone.Sent);
    Assert.Empty(usage.Entries);
  }

  [Fact]
  public async Task RequestOtp_when_name_required_and_provided_sends()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email, requireCustomerName: true);
    var mail = new CapturingBookingOtpEmailSender();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        mail,
        new CapturingPhoneOtpSender(),
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(fx.LeaseToken, fx.Appointment.Id, PhoneNumber: null, Email: "guest@example.com", FirstName: "Anna", LastName: "Kowalska"),
        ct);

    Assert.Single(mail.Sent);
  }

  [Fact]
  public async Task VerifyOtp_with_name_creates_customer_with_first_and_last_name()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email, requireCustomerName: true);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242", FirstName: "  Anna ", LastName: "Kowalska"),
        ct);

    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.NotNull(reloaded!.CustomerId);
    var customer = await fx.CustomerRepository.GetByEmail(fx.Tenant.Id, "guest@test.local", ct);
    Assert.NotNull(customer);
    Assert.Equal("Anna", customer!.FirstName);
    Assert.Equal("Kowalska", customer.LastName);
  }

  [Fact]
  public async Task VerifyOtp_when_name_required_but_missing_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email, requireCustomerName: true);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242", FirstName: null, LastName: "Kowalska"),
            ct));

    Assert.Equal(ErrorCodes.AppointmentOtpMissingName, ex.ErrorCode);
  }

  // „Umawiam ponownie": stały klient pomija imię już na etapie request-otp (flaga IsReturningCustomer).
  [Fact]
  public async Task RequestOtp_when_name_missing_but_returning_flag_set_sends()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone, requireCustomerName: true);
    var phone = new CapturingPhoneOtpSender();
    var handler = new RequestOtpCommandHandler(
        fx.AppointmentRepository,
        fx.Context,
        new CapturingBookingOtpEmailSender(),
        phone,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        new CapturingUsageRecorder(),
        new FakeSmsUsageGuard(),
        fx.Context,
        Microsoft.Extensions.Options.Options.Create(new BookingHoldOptions()),
        TimeProvider.System,
        fx.TenantService);

    await handler.Handle(
        new RequestOtpCommand(
            fx.LeaseToken, fx.Appointment.Id, PhoneNumber: "+48501111222", Email: null,
            FirstName: null, LastName: null, IsReturningCustomer: true),
        ct);

    Assert.Single(phone.Sent);
  }

  // „Umawiam ponownie" + zweryfikowany kontakt pasuje do istniejącego klienta z imieniem → potwierdza
  // bez podawania imienia i podpina pod istniejącego klienta (imię z jego rekordu, nie nadpisane).
  [Fact]
  public async Task VerifyOtp_returning_no_name_with_existing_customer_books()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone, requireCustomerName: true);

    var existing = Customer.CreateFromPublicBooking(
        fx.Tenant.Id, email: null, new PhoneNumber("+48501111222"), "Stała", "Klientka");
    fx.Context.Customers.Add(existing);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForPhone(new PhoneNumber("+48501111222"), "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242", FirstName: null, LastName: null),
        ct);

    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(existing.Id, reloaded!.CustomerId);
  }

  // „Umawiam ponownie", ale kontakt NIE pasuje do żadnego klienta → odrzucamy (poproś o imię).
  [Fact]
  public async Task VerifyOtp_returning_no_name_without_existing_customer_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone, requireCustomerName: true);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForPhone(new PhoneNumber("+48509999888"), "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(
            new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242", FirstName: null, LastName: null),
            ct));

    Assert.Equal(ErrorCodes.AppointmentOtpMissingName, ex.ErrorCode);
  }

  [Fact]
  public async Task VerifyOtp_with_instagram_nick_persists_normalized_nick_on_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242", InstagramNick: "  @anna_nails "),
        ct);

    var customer = await fx.CustomerRepository.GetByEmail(fx.Tenant.Id, "guest@test.local", ct);
    Assert.NotNull(customer);
    Assert.Equal("anna_nails", customer!.InstagramNick);
  }

  // ── Sesja zweryfikowanego kontaktu: mennica przy verify-otp + ścieżka confirm-with-session ──────

  private static ConfirmBookingWithSessionCommandHandler ConfirmHandler(Fixture fx, IBookingOtpProtection? protection = null) =>
      new(
          fx.AppointmentRepository,
          fx.CustomerRepository,
          fx.Context,
          protection ?? BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
          HttpAccessor(),
          fx.Context,
          new CapturingPublisher(),
          NullLogger<ConfirmBookingWithSessionCommandHandler>.Instance,
          TimeProvider.System,
          fx.TenantService,
          FakeInspirationUploadTokenService.Instance);

  private static async Task<SelfServiceOtp> SeedSessionAsync(
      Fixture fx, CancellationToken ct, string? email = "guest@test.local", string? phone = null, TimeSpan? lifetime = null)
  {
    var session = SelfServiceOtp.CreateVerifiedSession(
        fx.Tenant.Id, email, phone, DateTime.UtcNow,
        TimeSpan.FromMinutes(10), lifetime ?? TimeSpan.FromHours(2));
    fx.Context.SelfServiceOtps.Add(session);
    await fx.Context.SaveChangesAsync(ct);
    return session;
  }

  [Fact]
  public async Task VerifyOtp_mints_verified_contact_session_for_contact()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("guest@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);

    var handler = new VerifyOtpCommandHandler(
        fx.AppointmentRepository,
        fx.CustomerRepository,
        fx.TenantRepository,
        fx.Context,
        BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System),
        HttpAccessor(),
        fx.Context,
        new CapturingPublisher(),
        NullLogger<VerifyOtpCommandHandler>.Instance,
        TimeProvider.System,
        fx.TenantService,
        FakeInspirationUploadTokenService.Instance);

    var result = await handler.Handle(
        new VerifyOtpCommand(fx.LeaseToken, fx.Appointment.Id, "424242"),
        ct);

    Assert.NotNull(result.SessionToken);
    Assert.NotNull(result.SessionExpiresAtUtc);
    Assert.True(result.SessionExpiresAtUtc > DateTime.UtcNow.AddMinutes(115));

    var session = await fx.Context.SelfServiceOtps
        .FirstOrDefaultAsync(o => o.TenantId == fx.Tenant.Id && o.Email == "guest@test.local" && o.Consumed, ct);
    Assert.NotNull(session);
    Assert.Equal(result.SessionToken, session!.SessionToken);
  }

  [Fact]
  public async Task ConfirmWithSession_with_valid_email_session_books_and_links_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    var result = await ConfirmHandler(fx).Handle(
        new ConfirmBookingWithSessionCommand(
            fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
        ct);

    Assert.False(result.RequiresManualConfirmation);
    Assert.Null(result.SessionToken); // confirm nie wystawia nowej sesji
    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(AppointmentStatus.Booked, reloaded!.Status);
    Assert.Null(reloaded.Lease);
    Assert.NotNull(reloaded.CustomerId);
    var customer = await fx.CustomerRepository.GetByEmail(fx.Tenant.Id, "guest@test.local", ct);
    Assert.NotNull(customer);
  }

  [Fact]
  public async Task ConfirmWithSession_manual_mode_sets_pending()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Tenant.Update(fx.Tenant.Name, fx.Tenant.Slug, appointmentConfirmationMode: AppointmentConfirmationMode.Manual);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    var result = await ConfirmHandler(fx).Handle(
        new ConfirmBookingWithSessionCommand(
            fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
        ct);

    Assert.True(result.RequiresManualConfirmation);
    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(AppointmentStatus.Pending, reloaded!.Status);
  }

  [Fact]
  public async Task ConfirmWithSession_with_expired_session_throws_Forbidden()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local", lifetime: TimeSpan.FromHours(-1));

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));
  }

  [Fact]
  public async Task ConfirmWithSession_with_unknown_session_token_throws_Forbidden()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    await SeedSessionAsync(fx, ct, email: "guest@test.local");

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, Guid.NewGuid()),
            ct));
  }

  [Fact]
  public async Task ConfirmWithSession_with_wrong_lease_token_throws_Forbidden()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, Guid.NewGuid(), session.SessionToken!.Value),
            ct));
    Assert.Equal(ErrorCodes.AppointmentOtpInvalidLease, ex.ErrorCode);
  }

  [Fact]
  public async Task ConfirmWithSession_for_other_tenant_appointment_throws_NotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    // Bieżący tenant inny niż tenant wizyty → izolacja (404, bez ujawniania istnienia).
    fx.TenantService.TenantId = Guid.NewGuid();

    await Assert.ThrowsAsync<NotFoundException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));
  }

  [Fact]
  public async Task ConfirmWithSession_when_name_required_but_missing_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email, requireCustomerName: true);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value,
                FirstName: null, LastName: "Kowalska"),
            ct));
    Assert.Equal(ErrorCodes.AppointmentOtpMissingName, ex.ErrorCode);
  }

  // [H1] Cap potwierdzeń per sesja — po wyczerpaniu limitu confirm-with-session rzuca 429 (a NIE wysyła SMS).
  [Fact]
  public async Task ConfirmWithSession_over_session_confirmation_cap_throws_RateLimit()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    var protection = BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
    // Wyczerp budżet potwierdzeń tej sesji (5/h) — każdy AssertCan… rezerwuje slot atomowo.
    for (var i = 0; i < 5; i++)
    {
      protection.AssertCanConfirmWithSession(session.SessionToken!.Value, clientIp: null);
    }

    await Assert.ThrowsAsync<RateLimitExceededException>(() =>
        ConfirmHandler(fx, protection).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));

    // Hold nie został potwierdzony — wciąż AwaitingOtp.
    var reloaded = await fx.AppointmentRepository.GetByIdAsync(fx.Appointment.Id);
    Assert.Equal(AppointmentStatus.AwaitingOtp, reloaded!.Status);
  }

  // [H1/TOCTOU] Równoległe rezerwacje slotu tą samą sesją: dokładnie `cap` przechodzi, reszta 429.
  [Fact]
  public async Task AssertCanConfirmWithSession_is_atomic_under_concurrency()
  {
    const int cap = 5; // MaxConfirmWithSessionPerSessionPerHour
    var protection = BookingOtpProtectionFactory.Create(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
    var token = Guid.NewGuid();

    var granted = 0;
    var rejected = 0;
    var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
    {
      try
      {
        protection.AssertCanConfirmWithSession(token, clientIp: null);
        Interlocked.Increment(ref granted);
      }
      catch (RateLimitExceededException)
      {
        Interlocked.Increment(ref rejected);
      }
    }));
    await Task.WhenAll(tasks);

    // Bez atomowości (TOCTOU) granted byłoby > cap; lock gwarantuje dokładnie cap.
    Assert.Equal(cap, granted);
    Assert.Equal(50 - cap, rejected);
  }

  // [H2] Sesja kontaktu B nie może potwierdzić holdu, który przeszedł request-otp dla kontaktu A.
  [Fact]
  public async Task ConfirmWithSession_session_contact_differs_from_hold_otp_contact_throws_Forbidden()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.SetOtpVerification(
        OtpVerification.ForEmail("victim@test.local", "424242", DateTime.UtcNow.AddMinutes(10)));
    await fx.Context.SaveChangesAsync(ct);
    var session = await SeedSessionAsync(fx, ct, email: "attacker@test.local");

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));
    Assert.Equal(ErrorCodes.AppointmentSessionContactMismatch, ex.ErrorCode);
  }

  // [H2] Hold poza stanem AwaitingOtp (np. już Booked) nie może być potwierdzony tą ścieżką.
  [Fact]
  public async Task ConfirmWithSession_on_non_awaiting_otp_appointment_throws_Forbidden()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Email);
    fx.Appointment.ChangeStatus(AppointmentStatus.Booked);
    await fx.Context.SaveChangesAsync(ct);
    var session = await SeedSessionAsync(fx, ct, email: "guest@test.local");

    await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));
  }

  // [M2] Sesja na numerze nie-+48 → odrzucone (potwierdzenie wysłałoby SMS na numer zagraniczny/premium).
  [Fact]
  public async Task ConfirmWithSession_with_non_polish_session_phone_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    using var fx = new Fixture(CustomerVerificationChannel.Phone);
    // Poprawny numer brytyjski (valid E.164, ale nie-+48) → konstruktor PhoneNumber przejdzie,
    // a guard IsPolish odrzuci (nie-PL = drogie/zagraniczne SMS).
    var session = await SeedSessionAsync(fx, ct, email: null, phone: "+447911123456");

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        ConfirmHandler(fx).Handle(
            new ConfirmBookingWithSessionCommand(
                fx.Tenant.Slug, fx.Appointment.Id, fx.LeaseToken, session.SessionToken!.Value),
            ct));
    Assert.Equal(ErrorCodes.AppointmentOtpUnsupportedPhoneRegion, ex.ErrorCode);
  }
}
