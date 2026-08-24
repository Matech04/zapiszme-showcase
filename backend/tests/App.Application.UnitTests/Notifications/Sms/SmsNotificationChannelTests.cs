using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications;
using App.Infrastructure.Notifications.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>
/// SMS-CHANNEL-* — kanał SMS wysyła, gdy odbiorca ma numer telefonu, i pomija wysyłkę bez numeru
/// lub przy nieprawidłowym formacie. O tym, czy dany typ leci SMS-em, decyduje handler (kanał
/// komunikacji z klientem), więc kanał jest „głupi": numer jest → wysyłamy.
/// </summary>
public sealed class SmsNotificationChannelTests
{
  [Fact]
  public async Task SendAsync_WithPhone_CallsSmsApi()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Equal("48501234567", sent.To);
    Assert.False(string.IsNullOrWhiteSpace(sent.Text));
  }

  [Fact]
  public async Task SendAsync_OverMonthlyCap_SkipsSendAndDoesNotRecord()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var usage = new CapturingUsageRecorder();
    var channel = CreateChannel(api, usage, new FakeSmsUsageGuard(withinCap: false));

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    Assert.Empty(api.Calls);     // nic nie poszło do smsapi
    Assert.Empty(usage.Entries); // nie liczymy pominięcia jako wysyłki ani błędu
  }

  [Fact]
  public async Task SendAsync_WithPhone_RecordsSuccessfulUsageWithPoints()
  {
    var ct = TestContext.Current.CancellationToken;
    var usage = new CapturingUsageRecorder();
    var channel = CreateChannel(new RecordingSmsApiClient(), usage);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    var entry = Assert.Single(usage.Entries);
    Assert.Equal("Sms", entry.Channel);
    Assert.True(entry.Success);
  }

  [Fact]
  public async Task SendAsync_NoPhone_DoesNotRecordUsage()
  {
    var ct = TestContext.Current.CancellationToken;
    var usage = new CapturingUsageRecorder();
    var channel = CreateChannel(new RecordingSmsApiClient(), usage);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: null), ct);

    Assert.Empty(usage.Entries);
  }

  [Fact]
  public async Task SendAsync_NoPhone_DoesNotCallApi()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: null), ct);

    Assert.Empty(api.Calls);
  }

  [Fact]
  public async Task SendAsync_InvalidPhone_DoesNotCallApi()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "abc"), ct);

    Assert.Empty(api.Calls);
  }

  // ── SMS-CHANNEL-STATUS-* — kanał raportuje, czy faktycznie wysłał ──────────────────────

  [Fact]
  public async Task SendAsync_WithPhone_ReturnsSent()
  {
    var ct = TestContext.Current.CancellationToken;
    var channel = CreateChannel(new RecordingSmsApiClient());

    var status = await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    Assert.Equal(NotificationDeliveryStatus.Sent, status);
  }

  [Theory]
  [InlineData(null)]      // brak numeru
  [InlineData("abc")]     // nieparsowalny numer
  public async Task SendAsync_WithoutUsablePhone_ReturnsSkipped(string? phone)
  {
    var ct = TestContext.Current.CancellationToken;
    var channel = CreateChannel(new RecordingSmsApiClient());

    var status = await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone), ct);

    Assert.Equal(NotificationDeliveryStatus.Skipped, status);
  }

  [Fact]
  public async Task SendAsync_OverMonthlyCap_ReturnsSkipped()
  {
    var ct = TestContext.Current.CancellationToken;
    var channel = CreateChannel(new RecordingSmsApiClient(), guard: new FakeSmsUsageGuard(withinCap: false));

    var status = await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    Assert.Equal(NotificationDeliveryStatus.Skipped, status);
  }

  // ── SMS-CHANNEL-IDZDO-* — skracacz linków smsapi (obejście błędu 94) ───────────────────

  /// <summary>Domyślnie skracacz JEST włączony — surowy link nasze konto smsapi odrzuca (błąd 94).</summary>
  [Fact]
  public async Task SendAsync_ByDefault_WrapsActionUrlInIdzDoPlaceholder()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      DepositMessage(phone: "+48 501 234 567", actionUrl: "https://zps.me/p/ABC"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Contains("[%idzdo:https://zps.me/p/ABC%]", sent.Text, StringComparison.Ordinal);
    Assert.DoesNotContain("Oplac: https://zps.me/p/ABC", sent.Text, StringComparison.Ordinal);
  }

  /// <summary>Wyłącznik awaryjny (<c>Sms__UseIdzDoShortener=false</c>) wraca do surowego URL-a.</summary>
  [Fact]
  public async Task SendAsync_ShortenerDisabled_SendsRawUrl()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api, options: new SmsApiOptions { UseIdzDoShortener = false });

    await channel.SendAsync(
      DepositMessage(phone: "+48 501 234 567", actionUrl: "https://zps.me/p/ABC"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Contains("https://zps.me/p/ABC", sent.Text, StringComparison.Ordinal);
    Assert.DoesNotContain("idzdo", sent.Text, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SendAsync_ShortenerEnabled_NotificationWithoutActionUrl_LeavesTextUntouched()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.DoesNotContain("idzdo", sent.Text, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SendAsync_ShortenerEnabled_UrlMissingFromCustomTemplate_SendsTextUnchanged()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(
      api,
      resolver: new FakeSmsTemplateResolver("Salon: zadatek do oplaty, szczegoly u nas."));

    await channel.SendAsync(
      DepositMessage(phone: "+48 501 234 567", actionUrl: "https://zps.me/p/ABC"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Equal("Salon: zadatek do oplaty, szczegoly u nas.", sent.Text);
  }

  // ── SMS-CHANNEL-MACRO-* — wstrzyknięcie makra smsapi przez dane użytkownika ────────────

  /// <summary>
  /// Anonim rezerwuje jako „[%idzdo:evil.pl%]". Bez neutralizacji smsapi zinterpretowałby to jako
  /// własne makro i wstawił salonowi do SMS-a skrócony link phishingowy na zaufanej domenie idz.do.
  /// </summary>
  [Fact]
  public async Task SendAsync_CustomerNameWithSmsapiMacro_IsNeutralized()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      MessageWithCustomerName(NotificationType.NewBookingToSalon, "[%idzdo:evil.pl%]"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.DoesNotContain("[%", sent.Text, StringComparison.Ordinal);
    Assert.DoesNotContain("%]", sent.Text, StringComparison.Ordinal);
  }

  /// <summary>Neutralizacja treści nie może rozbroić NASZEGO makra doklejanego po sanityzacji.</summary>
  [Fact]
  public async Task SendAsync_MacroNeutralization_DoesNotBreakOwnShortener()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api);

    await channel.SendAsync(
      DepositMessage(phone: "+48 501 234 567", actionUrl: "https://zps.me/p/ABC"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Contains("[%idzdo:https://zps.me/p/ABC%]", sent.Text, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SendAsync_CustomTemplateWithMacro_IsNeutralized()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api, resolver: new FakeSmsTemplateResolver("Salon: [%idzdo:evil.pl%] zapraszamy"));

    await channel.SendAsync(
      Message(NotificationType.AppointmentReminderToCustomer, phone: "+48 501 234 567"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.DoesNotContain("[%", sent.Text, StringComparison.Ordinal);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static SmsNotificationChannel CreateChannel(
    ISmsApiClient api,
    INotificationUsageRecorder? usage = null,
    ISmsUsageGuard? guard = null,
    ISmsTemplateResolver? resolver = null,
    SmsApiOptions? options = null) =>
    new(
      api,
      usage ?? new CapturingUsageRecorder(),
      guard ?? new FakeSmsUsageGuard(),
      resolver ?? new FakeSmsTemplateResolver(null),
      Options.Create(options ?? new SmsApiOptions()),
      NullLogger<SmsNotificationChannel>.Instance);

  private static NotificationMessage Message(NotificationType type, string? phone) => new(
    Guid.NewGuid(),
    type,
    new NotificationRecipient(Email: "c@e.co", Phone: phone, UserId: null, DisplayName: "Jan"),
    "Temat",
    "Krótki",
    new NotificationPayload(
      SalonName: "Salon",
      CustomerName: "Jan",
      StaffName: "Ann",
      ServiceName: "Manicure",
      Date: new DateOnly(2026, 6, 1),
      StartTime: new TimeOnly(10, 0)));

  private static NotificationMessage MessageWithCustomerName(NotificationType type, string customerName) => new(
    Guid.NewGuid(),
    type,
    new NotificationRecipient(Email: null, Phone: "+48 501 234 567", UserId: null, DisplayName: customerName),
    "Temat",
    "Krótki",
    new NotificationPayload(
      SalonName: "Salon",
      CustomerName: customerName,
      StaffName: "Ann",
      ServiceName: "Manicure",
      Date: new DateOnly(2026, 6, 1),
      StartTime: new TimeOnly(10, 0)));

  private static NotificationMessage DepositMessage(string? phone, string actionUrl) => new(
    Guid.NewGuid(),
    NotificationType.DepositLinkToCustomer,
    new NotificationRecipient(Email: null, Phone: phone, UserId: null, DisplayName: "Jan"),
    "Temat",
    "Krótki",
    new NotificationPayload(
      SalonName: "Salon",
      CustomerName: "Jan",
      ServiceName: "Manicure",
      Date: new DateOnly(2026, 6, 1),
      StartTime: new TimeOnly(10, 0),
      ActionUrl: actionUrl,
      AmountText: "50 zł"));

  private sealed class RecordingSmsApiClient : ISmsApiClient
  {
    public List<(string To, string Text)> Calls { get; } = new();
    public Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct)
    {
      Calls.Add((to, text));
      return Task.FromResult(new SmsSendResult("id", "QUEUE", 0m));
    }
  }

  private sealed class FakeSmsTemplateResolver : ISmsTemplateResolver
  {
    private readonly string? _resolved;
    public FakeSmsTemplateResolver(string? resolved) => _resolved = resolved;
    public Task<string?> TryResolveAsync(Guid tenantId, NotificationType type, NotificationPayload payload, CancellationToken ct) =>
      Task.FromResult(_resolved);
  }

  [Fact]
  public async Task SendAsync_ApprovedCustomTemplate_UsesCustomTextInsteadOfHardcoded()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    var channel = CreateChannel(api, resolver: new FakeSmsTemplateResolver("Custom tresc salonu"));

    await channel.SendAsync(
      Message(NotificationType.BookingConfirmationToCustomer, phone: "+48 501 234 567"), ct);

    var sent = Assert.Single(api.Calls);
    Assert.Equal("Custom tresc salonu", sent.Text);
  }

  [Fact]
  public async Task SendAsync_NoApprovedTemplate_FallsBackToHardcodedBuilder()
  {
    var ct = TestContext.Current.CancellationToken;
    var api = new RecordingSmsApiClient();
    // resolver zwraca null (brak/niezatwierdzony szablon) → fallback do SmsTextBuilder.
    var channel = CreateChannel(api, resolver: new FakeSmsTemplateResolver(null));

    await channel.SendAsync(
      Message(NotificationType.BookingConfirmationToCustomer, phone: "+48 501 234 567"), ct);

    var sent = Assert.Single(api.Calls);
    // Hardcoded szablon zawiera nazwę salonu i potwierdzenie — custom by tego nie miał.
    Assert.Contains("Salon", sent.Text);
    Assert.Contains("potwierdzona", sent.Text);
  }
}
