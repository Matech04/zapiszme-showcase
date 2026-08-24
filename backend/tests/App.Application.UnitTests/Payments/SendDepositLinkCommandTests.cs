using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Payments.Commands.SendDepositLink;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using App.Application.UnitTests.Support;

namespace App.Application.UnitTests.Payments;

/// <summary>
/// DEPOSIT-SEND-* — „wyślij klientowi link do zadatku" to jawna akcja personelu, nie powiadomienie
/// tła. Dispatcher jest best-effort i połyka awarie kanałów, więc handler MUSI sam sprawdzić, czy
/// wybrany kanał faktycznie dostarczył wiadomość. Regresja z produkcji: smsapi odrzucał SMS z linkiem
/// (błąd 94), a panel i tak pokazywał „Wysłano".
/// </summary>
public sealed class SendDepositLinkCommandTests
{
  [Fact]
  public async Task Handle_SmsChannelFailed_ThrowsDepositSendFailed()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Failed, "SmsApiException")));

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));

    Assert.Equal(ErrorCodes.DepositSendFailed, ex.ErrorCode);
  }

  [Fact]
  public async Task Handle_SmsChannelTimedOut_ThrowsDepositSendFailed()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.TimedOut, "timeout")));

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));
  }

  /// <summary>Kanał SMS wyłączony (brak tokenu = brak rejestracji w DI) — brak wpisu w wyniku.</summary>
  [Fact]
  public async Task Handle_SmsChannelNotRegistered_ThrowsDepositSendFailed()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(CustomerVerificationChannel.Phone, new StubDispatcher());

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));
  }

  [Fact]
  public async Task Handle_SmsChannelSent_ReturnsPhoneChannel()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent)));

    var result = await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    Assert.Equal(nameof(CustomerVerificationChannel.Phone), result.Channel);
  }

  /// <summary>Po udanej wysyłce zapisujemy znacznik — panel pokazuje „wysłano" zamiast zgadywać.</summary>
  [Fact]
  public async Task Handle_SmsChannelSent_StampsDepositLinkSentAt()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, db) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent)));

    await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    var appointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId, ct);
    Assert.NotNull(appointment.DepositLinkSentAtUtc);
    Assert.Equal(NotificationChannelKind.Sms.ToString(), appointment.DepositLinkSentChannel);
  }

  [Fact]
  public async Task Handle_SmsChannelFailed_DoesNotStampDepositLinkSentAt()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, db) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Failed, "SmsApiException")));

    await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));

    var appointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Null(appointment.DepositLinkSentAtUtc);
  }

  /// <summary>Demo-tenant: dispatcher celowo wycisza SMS/e-mail. To nie awaria — panel ma pokazać sukces.</summary>
  [Fact]
  public async Task Handle_ChannelSuppressedForDemoTenant_Succeeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Suppressed, "demo-tenant")));

    var result = await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    Assert.Equal(nameof(CustomerVerificationChannel.Phone), result.Channel);
  }

  /// <summary>Kanał e-mail padł, SMS „wysłany" — ale klient jest na e-mailu, więc to porażka.</summary>
  [Fact]
  public async Task Handle_EmailChannelFailed_IgnoresOtherChannelsSuccess()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Email,
      new StubDispatcher(
        new NotificationChannelOutcome(NotificationChannelKind.Email, NotificationDeliveryStatus.Failed, "SmtpException"),
        new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent)));

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));

    Assert.Equal(ErrorCodes.DepositSendFailed, ex.ErrorCode);
  }

  // ── Regresje z preflightu (2026-07-09) ─────────────────────────────────────────────────

  /// <summary>
  /// SMS wyszedł i jest opłacony; pad zapisu znacznika NIE może zamienić się w „nie wysłano" —
  /// personel ponowiłby akcję i zapłacił za drugą wiadomość.
  /// </summary>
  [Fact]
  public async Task Handle_SaveFailsAfterSuccessfulSend_StillReportsSuccess()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent)),
      failSaveChanges: true);

    var result = await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    Assert.Equal(nameof(CustomerVerificationChannel.Phone), result.Channel);
  }

  [Fact]
  public async Task Handle_ResendWithinCooldown_ThrowsDepositSendCooldown()
  {
    var ct = TestContext.Current.CancellationToken;
    var (handler, appointmentId, _) = CreateHandler(
      CustomerVerificationChannel.Phone,
      new StubDispatcher(new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent)));

    await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(
      () => handler.Handle(new SendDepositLinkCommand(appointmentId), ct));

    Assert.Equal(ErrorCodes.DepositSendCooldown, ex.ErrorCode);
  }

  [Fact]
  public async Task Handle_ResendAfterCooldownElapsed_SendsAgain()
  {
    var ct = TestContext.Current.CancellationToken;
    var dispatcher = new CountingDispatcher(
      new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent));
    var (handler, appointmentId, db) = CreateHandler(CustomerVerificationChannel.Phone, dispatcher);

    await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    // Cofamy znacznik poza okno cooldownu (3 min) — bez zależności od realnego zegara w teście.
    var appointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId, ct);
    appointment.MarkDepositLinkSent(DateTime.UtcNow.AddMinutes(-10), NotificationChannelKind.Sms.ToString());
    await db.SaveChangesAsync(ct);

    await handler.Handle(new SendDepositLinkCommand(appointmentId), ct);

    Assert.Equal(2, dispatcher.Calls);
  }

  /// <summary>
  /// Izolacja tej ścieżki wisi na globalnym query filterze Appointment. Gdyby ktoś zamienił ładowanie
  /// wizyty na IgnoreQueryFilters, personel salonu A wysłałby link do wizyty salonu B na jego koszt.
  /// </summary>
  [Fact]
  public async Task Handle_AppointmentOfAnotherTenant_ThrowsNotFound()
  {
    var ct = TestContext.Current.CancellationToken;
    var dbName = Guid.NewGuid().ToString();

    var tenantB = new Tenant("Obcy salon", $"obcy-{Guid.NewGuid():N}");
    tenantB.Update(tenantB.Name, tenantB.Slug, CustomerVerificationChannel.Phone, depositSettings: null);
    var foreignAppointmentId = SeedTenantWithDepositLink(dbName, tenantB);

    // Kontekst i handler należą do tenanta A, wizyta do tenanta B.
    var tenantA = new Tenant("Nasz salon", $"nasz-{Guid.NewGuid():N}");
    var currentTenant = new FakeCurrentTenantService(tenantA.Id);
    var db = new ApplicationDbContext(InMemoryOptions(dbName), currentTenant);
    var dispatcher = new CountingDispatcher(
      new NotificationChannelOutcome(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent));
    var handler = new SendDepositLinkCommandHandler(
      currentTenant, new PermissiveStaffAccessPolicy(), db, dispatcher, new FakeSmsUsageGuard(),
      NullLogger<SendDepositLinkCommandHandler>.Instance);

    await Assert.ThrowsAsync<NotFoundException>(
      () => handler.Handle(new SendDepositLinkCommand(foreignAppointmentId), ct));

    Assert.Equal(0, dispatcher.Calls); // żaden SMS nie poszedł na koszt obcego salonu
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static DbContextOptions<ApplicationDbContext> InMemoryOptions(string dbName) =>
    new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;

  private static Guid SeedTenantWithDepositLink(string dbName, Tenant tenant)
  {
    var owner = new FakeCurrentTenantService(tenant.Id);
    using var db = new ApplicationDbContext(InMemoryOptions(dbName), owner);

    var customer = new Customer(tenant.Id, "Ewa", "Nowak", "ewa@obcy.local", new PhoneNumber("+48501111222"), string.Empty);
    var appointment = new Appointment(
      tenant.Id, Guid.NewGuid(), Guid.NewGuid(), customer.Id,
      new DateOnly(2026, 12, 21), new TimeOnly(11, 0), new TimeOnly(11, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
    appointment.GenerateDepositLink(
      new Money(30m, "PLN"), "cs_obcy", "https://zps.me/p/XYZ", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    db.Tenants.Add(tenant);
    db.Customers.Add(customer);
    db.Appointments.Add(appointment);
    db.SaveChanges();

    return appointment.Id;
  }

  private static (SendDepositLinkCommandHandler Handler, Guid AppointmentId, ApplicationDbContext Db) CreateHandler(
    CustomerVerificationChannel channel, INotificationDispatcher dispatcher, bool failSaveChanges = false)
  {
    var tenant = new Tenant("Salon", $"salon-{Guid.NewGuid():N}");
    tenant.Update(tenant.Name, tenant.Slug, channel, depositSettings: null);

    var options = InMemoryOptions(Guid.NewGuid().ToString());
    var currentTenant = new FakeCurrentTenantService(tenant.Id);
    var db = failSaveChanges
      ? new FailingSaveDbContext(options, currentTenant)
      : new ApplicationDbContext(options, currentTenant);

    var customer = new Customer(tenant.Id, "Jan", "Kowalski", "jan@klient.local", new PhoneNumber("+48501234567"), string.Empty);
    var appointment = new Appointment(
      tenant.Id, Guid.NewGuid(), Guid.NewGuid(), customer.Id,
      new DateOnly(2026, 12, 20), new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
    appointment.GenerateDepositLink(
      new Money(30m, "PLN"), "cs_test", "https://zps.me/p/ABC", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    db.Tenants.Add(tenant);
    db.Customers.Add(customer);
    db.Appointments.Add(appointment);
    db.SaveChanges();

    var handler = new SendDepositLinkCommandHandler(
      currentTenant, new PermissiveStaffAccessPolicy(), db, dispatcher, new FakeSmsUsageGuard(),
      NullLogger<SendDepositLinkCommandHandler>.Instance);
    return (handler, appointment.Id, db);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  /// <summary>Seeding (sync <c>SaveChanges</c>) działa; padają dopiero zapisy handlera (async).</summary>
  private sealed class FailingSaveDbContext : ApplicationDbContext
  {
    public FailingSaveDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenant)
      : base(options, tenant) { }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
      throw new DbUpdateException("Symulowany pad zapisu po udanej wysyłce.");
  }

  private sealed class CountingDispatcher : INotificationDispatcher
  {
    private readonly NotificationChannelOutcome[] _outcomes;
    public CountingDispatcher(params NotificationChannelOutcome[] outcomes) => _outcomes = outcomes;
    public int Calls { get; private set; }

    public Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct)
    {
      Calls++;
      return Task.FromResult(new NotificationDispatchResult(_outcomes));
    }
  }

  private sealed class FakeSmsUsageGuard : ISmsUsageGuard
  {
    public Task<bool> IsWithinMonthlyCapAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(true);
  }

  private sealed class StubDispatcher : INotificationDispatcher
  {
    private readonly NotificationChannelOutcome[] _outcomes;
    public StubDispatcher(params NotificationChannelOutcome[] outcomes) => _outcomes = outcomes;

    public Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct) =>
      Task.FromResult(new NotificationDispatchResult(_outcomes));
  }
}
