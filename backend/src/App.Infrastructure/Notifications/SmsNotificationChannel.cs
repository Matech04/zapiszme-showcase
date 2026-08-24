using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Kanał powiadomień SMS (smsapi.pl). Wysyła, gdy odbiorca ma numer telefonu — o tym, czy dany
/// typ powiadomienia w ogóle leci i którym kanałem, decyduje handler: powiadomienia do klienta
/// idą kanałem komunikacji z klientem (<c>Tenant.CustomerVerificationChannel</c>), więc handler
/// ustawia <c>Recipient.Phone</c> tylko wtedy, gdy klient jest „na SMS-ie".
/// Awarie API wywołują wyjątek — <see cref="NotificationDispatcher"/> łapie i izoluje od innych kanałów.
/// </summary>
public sealed class SmsNotificationChannel : INotificationChannel
{
  private readonly ISmsApiClient _smsApi;
  private readonly INotificationUsageRecorder _usage;
  private readonly ISmsUsageGuard _usageGuard;
  private readonly ISmsTemplateResolver _templateResolver;
  private readonly IOptions<SmsApiOptions> _options;
  private readonly ILogger<SmsNotificationChannel> _logger;

  public SmsNotificationChannel(
    ISmsApiClient smsApi,
    INotificationUsageRecorder usage,
    ISmsUsageGuard usageGuard,
    ISmsTemplateResolver templateResolver,
    IOptions<SmsApiOptions> options,
    ILogger<SmsNotificationChannel> logger)
  {
    _smsApi = smsApi;
    _usage = usage;
    _usageGuard = usageGuard;
    _templateResolver = templateResolver;
    _options = options;
    _logger = logger;
  }

  public NotificationChannelKind Kind => NotificationChannelKind.Sms;

  public async Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(message.Recipient.Phone))
    {
      _logger.LogDebug("Pomijam SMS dla {Type} — brak numeru telefonu odbiorcy.", message.Type);
      return NotificationDeliveryStatus.Skipped;
    }

    var to = PhoneNumberNormalizer.TryNormalize(message.Recipient.Phone);
    if (to is null)
    {
      _logger.LogWarning(
        "SMS: nieprawidłowy format telefonu '{Phone}' dla {Type} (tenant {TenantId}).",
        message.Recipient.Phone, message.Type, message.TenantId);
      return NotificationDeliveryStatus.Skipped;
    }

    // Anti-abuse: po osiągnięciu miesięcznego twardego limitu SMS pomijamy wysyłkę powiadomienia
    // (best-effort kanał — nie rejestrujemy jako wysłane ani jako błąd, bo nic nie poszło do smsapi).
    if (!await _usageGuard.IsWithinMonthlyCapAsync(message.TenantId, ct))
    {
      _logger.LogWarning(
        "SMS pominięty (twardy limit miesięczny osiągnięty): {Type}, tenant {TenantId}.",
        message.Type, message.TenantId);
      return NotificationDeliveryStatus.Skipped;
    }

    // Najpierw spróbuj ZATWIERDZONEGO custom szablonu salonu; brak/niezatwierdzony → fallback do
    // hardcoded SmsTextBuilder (zero regresji dla salonów bez własnych szablonów).
    var text = await _templateResolver.TryResolveAsync(message.TenantId, message.Type, message.Payload, ct)
               ?? SmsTextBuilder.Build(message.Type, message.Payload);

    // Rozbrajamy makra smsapi TU, a nie tylko w sanityzacji: kanał jest jedynym miejscem, przez które
    // przechodzi każda treść (hardcoded i custom), i nie chcemy ufać, że każdy resolver zawoła Sanitize.
    // Kolejność względem ApplyLinkShortener jest krytyczna — najpierw rozbroić cudze, potem dokleić własne.
    text = SmsTextBuilder.NeutralizeMacros(text);
    text = ApplyLinkShortener(text, message.Payload.ActionUrl, message.Type);

    SmsSendResult result;
    try
    {
      result = await _smsApi.SendAsync(to, text, ct);
    }
    catch
    {
      // Wysyłka padła — zapisz nieudaną próbę (bez kosztu) i przekaż wyjątek dalej,
      // żeby dispatcher zachował izolację kanałów.
      await _usage.RecordSmsAsync(message.TenantId, message.Type, success: false, points: 0m, ct);
      throw;
    }

    await _usage.RecordSmsAsync(message.TenantId, message.Type, success: true, points: result.Points, ct);
    _logger.LogInformation(
      "SMS wysłany: {Type} → {To}, id={Id}, status={Status}, points={Points}.",
      message.Type, Mask(to), result.Id, result.Status, result.Points);

    return NotificationDeliveryStatus.Sent;
  }

  /// <summary>
  /// Opakowuje link akcji w skracacz smsapi (<c>[%idzdo:url%]</c>), gdy włączona jest opcja
  /// <see cref="SmsApiOptions.UseIdzDoShortener"/>. Powód: smsapi blokuje SMS-y z surowym URL-em
  /// (błąd 94 „Not allowed to send messages with link") do czasu przyznania kontu zgody na linki;
  /// skrócony link stoi na zaufanej domenie idz.do. Wołane PO sanityzacji, żeby limit 140 znaków
  /// liczył się od realnego URL-a (link idz.do jest krótszy), a nie od placeholdera.
  /// </summary>
  private string ApplyLinkShortener(string text, string? actionUrl, NotificationType type)
  {
    if (!_options.Value.UseIdzDoShortener || string.IsNullOrWhiteSpace(actionUrl))
    {
      return text;
    }

    if (!text.Contains(actionUrl, StringComparison.Ordinal))
    {
      // Sanityzacja przycięła URL (za długi prefiks szablonu) albo salon usunął {link} z custom
      // szablonu. Nie zgadujemy — wysyłamy jak jest i zostawiamy ślad, bo bez linku SMS jest bezużyteczny.
      _logger.LogWarning(
        "SMS {Type}: nie znalazłem linku akcji w treści — skracacz idz.do pominięty.", type);
      return text;
    }

    return text.Replace(actionUrl, $"[%idzdo:{actionUrl}%]", StringComparison.Ordinal);
  }

  private static string Mask(string phone) =>
    phone.Length <= 6 ? phone : phone[..6] + new string('*', phone.Length - 6);
}
