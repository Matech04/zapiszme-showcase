using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Email;
using App.Infrastructure.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-INFRA-* — dispatcher izoluje błędy kanałów, wycisza kanały wychodzące dla demo-tenantów,
/// a kanał e-mail pomija odbiorców bez adresu.
/// </summary>
public sealed class NotificationInfrastructureTests
{
  [Fact]
  public async Task Dispatcher_OneFailingChannel_DoesNotBlockOthers()
  {
    await using var db = NewDb(Guid.NewGuid());
    var ok = new CapturingChannel(NotificationChannelKind.InApp);
    var dispatcher = new NotificationDispatcher(
      new INotificationChannel[] { new ThrowingChannel(), ok },
      db,
      NullLogger<NotificationDispatcher>.Instance);

    await dispatcher.DispatchAsync(Message("ok@e.co"), TestContext.Current.CancellationToken);

    Assert.Single(ok.Received);
  }

  [Fact]
  public async Task Dispatcher_DemoTenant_SkipsEmailAndSms_KeepsInApp()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Demo Studio", "demo-test", "Europe/Warsaw", "PLN");
    tenant.MarkAsDemo(DateTime.UtcNow);

    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var email = new CapturingChannel(NotificationChannelKind.Email);
    var sms = new CapturingChannel(NotificationChannelKind.Sms);
    var inApp = new CapturingChannel(NotificationChannelKind.InApp);
    var dispatcher = new NotificationDispatcher(
      new INotificationChannel[] { email, sms, inApp },
      db,
      NullLogger<NotificationDispatcher>.Instance);

    await dispatcher.DispatchAsync(Message("client@e.co", tenant.Id), ct);

    Assert.Empty(email.Received);
    Assert.Empty(sms.Received);
    Assert.Single(inApp.Received);
  }

  [Fact]
  public async Task Dispatcher_AttachesTenantBrandToMessage()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = new Tenant("Studio Ani", "studio-ani", "Europe/Warsaw", "PLN");
    tenant.Update("Studio Ani", "studio-ani", bookingCalendarColorHex: "#0EA5E9");

    await using var db = NewDb(tenant.Id);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);

    var channel = new CapturingChannel(NotificationChannelKind.Email);
    var dispatcher = new NotificationDispatcher(
      [channel],
      db,
      NullLogger<NotificationDispatcher>.Instance);

    await dispatcher.DispatchAsync(Message("client@e.co", tenant.Id), ct);

    var brand = Assert.Single(channel.Received).Brand;
    Assert.NotNull(brand);
    Assert.Equal("Studio Ani", brand.Name);
    Assert.Equal("#0EA5E9", brand.AccentHex);
  }

  [Fact]
  public async Task EmailChannel_NoRecipientEmail_DoesNotCallTransport()
  {
    var transport = new RecordingTransport();
    var channel = new EmailNotificationChannel(transport, new CapturingUsageRecorder(), NullLogger<EmailNotificationChannel>.Instance);

    await channel.SendAsync(Message(email: null), TestContext.Current.CancellationToken);

    Assert.Empty(transport.Sent);
  }

  [Fact]
  public async Task EmailChannel_WithRecipientEmail_SendsOnceWithSubject()
  {
    var transport = new RecordingTransport();
    var channel = new EmailNotificationChannel(transport, new CapturingUsageRecorder(), NullLogger<EmailNotificationChannel>.Instance);

    await channel.SendAsync(Message("client@e.co"), TestContext.Current.CancellationToken);

    var sent = Assert.Single(transport.Sent);
    Assert.Equal("client@e.co", sent.To);
    Assert.Equal("Temat testowy", sent.Subject);
    Assert.False(string.IsNullOrWhiteSpace(sent.Html));
    Assert.False(string.IsNullOrWhiteSpace(sent.Text));
  }

  [Fact]
  public async Task EmailChannel_UsesTenantBrand_WhenDispatcherSetIt()
  {
    var transport = new RecordingTransport();
    var channel = new EmailNotificationChannel(transport, new CapturingUsageRecorder(), NullLogger<EmailNotificationChannel>.Instance);
    var message = Message("client@e.co") with { Brand = new EmailBrand("Studio Ani", "#0EA5E9") };

    await channel.SendAsync(message, TestContext.Current.CancellationToken);

    var sent = Assert.Single(transport.Sent);
    Assert.Contains("Studio Ani", sent.Html, StringComparison.Ordinal);
    Assert.Contains("#0EA5E9", sent.Html, StringComparison.Ordinal);
  }

  [Fact]
  public async Task EmailChannel_NoBrand_FallsBackToSalonNameFromPayload()
  {
    var transport = new RecordingTransport();
    var channel = new EmailNotificationChannel(transport, new CapturingUsageRecorder(), NullLogger<EmailNotificationChannel>.Instance);

    await channel.SendAsync(Message("client@e.co"), TestContext.Current.CancellationToken);

    var sent = Assert.Single(transport.Sent);
    Assert.Contains("Salon", sent.Html, StringComparison.Ordinal);
  }

  // Asercje na dokumencie, nie na HTML-u: nagłówek/stopka layoutu mogą zawierać dowolne słowa,
  // więc `DoesNotContain("Klient", html)` byłoby testem szablonu, nie logiki pomijania wierszy.
  [Fact]
  public void BuildDocument_NewBookingToSalon_KeepsProvidedContactRows()
  {
    var document = EmailNotificationChannel.BuildDocument(
      NotificationType.NewBookingToSalon,
      SalonBookingPayload(customerFullName: "Jan Kowalski", customerPhone: "+48501234567", customerEmail: null));

    Assert.Equal("Jan Kowalski", Row(document, "Klient"));
    Assert.Equal("+48501234567", Row(document, "Telefon"));
    // Kanał e-mailowy nie był użyty przy rezerwacji → wiersz „E-mail" bez wartości, renderer go pominie.
    Assert.Null(Row(document, "E-mail"));
  }

  [Fact]
  public void BuildDocument_NewBookingToSalon_NoName_OmitsCustomerRowButKeepsContact()
  {
    var document = EmailNotificationChannel.BuildDocument(
      NotificationType.NewBookingToSalon,
      SalonBookingPayload(customerFullName: "", customerPhone: null, customerEmail: "klient@e.co"));

    Assert.True(string.IsNullOrEmpty(Row(document, "Klient")));
    Assert.Equal("klient@e.co", Row(document, "E-mail"));
  }

  private static string? Row(EmailDocument document, string label) =>
    document.Details.Single(d => d.Label == label).Value;

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static NotificationPayload SalonBookingPayload(
    string customerFullName, string? customerPhone, string? customerEmail) => new(
    SalonName: "Salon",
    StaffName: "Ann Smith",
    ServiceName: "Strzyżenie",
    Date: new DateOnly(2026, 6, 1),
    StartTime: new TimeOnly(10, 0),
    CustomerFullName: customerFullName,
    CustomerPhone: customerPhone,
    CustomerEmail: customerEmail);


  private static ApplicationDbContext NewDb(Guid tenantId)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
  }

  private static NotificationMessage Message(string? email) => Message(email, Guid.NewGuid());

  private static NotificationMessage Message(string? email, Guid tenantId) => new(
    tenantId,
    NotificationType.BookingConfirmationToCustomer,
    new NotificationRecipient(email, null, null, "Jan Kowalski"),
    "Temat testowy",
    "Krótki tekst",
    new NotificationPayload(
      SalonName: "Salon",
      CustomerName: "Jan Kowalski",
      StaffName: "Ann Smith",
      ServiceName: "Strzyżenie",
      Date: new DateOnly(2026, 6, 1),
      StartTime: new TimeOnly(10, 0)));

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class CapturingChannel : INotificationChannel
  {
    public CapturingChannel(NotificationChannelKind kind) => Kind = kind;
    public NotificationChannelKind Kind { get; }
    public List<NotificationMessage> Received { get; } = new();
    public Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
    {
      Received.Add(message);
      return Task.FromResult(NotificationDeliveryStatus.Sent);
    }
  }

  private sealed class ThrowingChannel : INotificationChannel
  {
    public NotificationChannelKind Kind => NotificationChannelKind.Sms;
    public Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
      => throw new InvalidOperationException("boom");
  }

  private sealed class RecordingTransport : IEmailTransport
  {
    public List<(string To, string Subject, string Html, string Text)> Sent { get; } = new();
    public string? FeedbackRecipientEmail => null;
    public Task SendAsync(string toEmail, string subject, EmailBody body, CancellationToken ct = default)
    {
      Sent.Add((toEmail, subject, body.Html, body.Text));
      return Task.CompletedTask;
    }
  }
}
