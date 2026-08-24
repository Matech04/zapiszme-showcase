using System.Text.Encodings.Web;
using System.Text.Json;
using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Notifications.Push;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Kanał Web Push: wysyła powiadomienie na wszystkie subskrypcje urządzeń odbiorcy
/// (<c>Recipient.UserId</c>). Wiadomości dla klienta końcowego (UserId == null) pomijane —
/// klient nie ma panelu (analogicznie do <see cref="InAppNotificationChannel"/>).
///
/// Treść jest budowana z danych rezerwacji (payload) specjalnie pod push: tytuł = „co + kto",
/// treść = „usługa · kiedy" (przyjazna data w strefie salonu), bez powtórzeń. Payload w formacie
/// oczekiwanym przez <c>ngsw-worker.js</c> Angulara (klik → openWindow deep-link).
/// Martwe subskrypcje (404/410) kasujemy. Best-effort — izolowane przez NotificationDispatcher.
/// </summary>
public sealed class WebPushNotificationChannel : INotificationChannel
{
  // Relaxed encoder — polskie znaki w payloadzie zostają literalne (bez \uXXXX). Payload to
  // ciało żądania JSON do push-service (nie trafia do HTML), więc jest to bezpieczne i czytelniejsze.
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };
  private static readonly int[] VibrationPattern = [80, 40, 80];

  private readonly IApplicationDbContext _context;
  private readonly IWebPushSender _sender;
  private readonly ILogger<WebPushNotificationChannel> _logger;

  public WebPushNotificationChannel(
    IApplicationDbContext context,
    IWebPushSender sender,
    ILogger<WebPushNotificationChannel> logger)
  {
    _context = context;
    _sender = sender;
    _logger = logger;
  }

  public NotificationChannelKind Kind => NotificationChannelKind.WebPush;

  public async Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
  {
    if (message.Recipient.UserId is not { } userId)
    {
      _logger.LogDebug(
        "Pomijam web-push dla {Type} — odbiorca bez konta panelu (wiadomość dla klienta).",
        message.Type);
      return NotificationDeliveryStatus.Skipped;
    }

    // IgnoreQueryFilters + jawny TenantId: kanał bywa wołany z background jobów (przypomnienia)
    // bez ambient tenanta — jawny filtr zachowuje izolację niezależnie od kontekstu żądania.
    var subscriptions = await _context.PushSubscriptions
      .IgnoreQueryFilters()
      .Where(s => s.TenantId == message.TenantId && s.UserId == userId)
      .ToListAsync(ct);

    if (subscriptions.Count == 0)
    {
      return NotificationDeliveryStatus.Skipped;
    }

    var today = await ResolveTenantTodayAsync(message.TenantId, ct);
    var payload = BuildPayload(message, today);
    var expiredIds = new List<Guid>();

    foreach (var sub in subscriptions)
    {
      var result = await _sender.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
      if (result == WebPushSendResult.Expired)
      {
        expiredIds.Add(sub.Id);
      }
    }

    if (expiredIds.Count > 0)
    {
      var dead = subscriptions.Where(s => expiredIds.Contains(s.Id));
      _context.PushSubscriptions.RemoveRange(dead);
      await _context.SaveChangesAsync(ct);
      _logger.LogDebug("Skasowano {Count} martwych subskrypcji web-push.", expiredIds.Count);
    }

    return NotificationDeliveryStatus.Sent;
  }

  private async Task<DateOnly> ResolveTenantTodayAsync(Guid tenantId, CancellationToken ct)
  {
    var tzId = await _context.Tenants
      .AsNoTracking()
      .IgnoreQueryFilters()
      .Where(t => t.Id == tenantId)
      .Select(t => t.TimeZoneId)
      .FirstOrDefaultAsync(ct);

    try
    {
      var tz = TimeZoneInfo.FindSystemTimeZoneById(
        string.IsNullOrWhiteSpace(tzId) ? "Europe/Warsaw" : tzId);
      return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }
    catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
      // Brak tzdata / nieznana strefa — degradujemy do UTC (data względna może być o kilka
      // godzin nietrafiona wokół północy). Nie blokujemy wysyłki.
      return DateOnly.FromDateTime(DateTime.UtcNow);
    }
  }

  private string BuildPayload(NotificationMessage message, DateOnly today)
  {
    var (title, body) = BuildContent(message, today);

    // Deep-link do wizyty w kalendarzu (spójny z appointmentNotificationTarget w dashboardzie).
    var url = message.ReferenceId is { } refId
      ? $"/admin/schedule?appointment={refId}"
      : "/admin";

    // tag = wizyta: kolejna zmiana tej samej rezerwacji odświeża baner zamiast dokładać nowy.
    var tag = message.ReferenceId?.ToString() ?? $"type-{(int)message.Type}";

    var payload = new
    {
      notification = new
      {
        title,
        body,
        icon = "/icons/icon-192.png",
        badge = "/icons/badge-72.png",
        tag,
        renotify = true,
        vibrate = VibrationPattern,
        data = new
        {
          onActionClick = new
          {
            @default = new { operation = "openWindow", url },
          },
        },
      },
    };

    return JsonSerializer.Serialize(payload, JsonOptions);
  }

  /// <summary>
  /// Treść push per typ, budowana z payloadu: tytuł „co + kto", treść „usługa · kiedy".
  /// Gdy brakuje danych do ładnego złożenia — fallback do Subject/ShortText komunikatu.
  /// </summary>
  private static (string Title, string Body) BuildContent(NotificationMessage message, DateOnly today)
  {
    var p = message.Payload;
    var when = FriendlyWhen(p.Date, p.StartTime, today);
    if (when is null)
    {
      // Brak daty/godziny w payloadzie (nietypowe) — użyj gotowych stringów komunikatu.
      return (message.Subject, message.ShortText);
    }

    var service = string.IsNullOrWhiteSpace(p.ServiceName) ? "Wizyta" : p.ServiceName!;
    var customer = CustomerLabel(p);
    var who = customer is null ? string.Empty : $" — {customer}";

    return message.Type switch
    {
      NotificationType.NewBookingToSalon =>
        ($"Nowa rezerwacja{who}", $"{service} · {when}"),

      NotificationType.CancellationToSalon =>
        ($"Odwołana wizyta{who}", $"{service} · {when}"),

      NotificationType.RescheduleToSalon =>
        ($"Zmiana terminu{who}", BuildRescheduleBody(service, p, today, when)),

      NotificationType.AwaitingConfirmationToSalon =>
        (customer is null ? "Rezerwacja do potwierdzenia" : $"Do potwierdzenia — {customer}",
         $"{service} · {when}"),

      // Inny typ trafił na push (nie powinien) — bezpieczny fallback.
      _ => (message.Subject, message.ShortText),
    };
  }

  private static string BuildRescheduleBody(string service, NotificationPayload p, DateOnly today, string newWhen)
  {
    var oldWhen = FriendlyWhen(p.OldDate, p.OldStartTime, today);
    return oldWhen is null
      ? $"{service} · {newWhen}"
      : $"{service} · {oldWhen} → {newWhen}";
  }

  /// <summary>
  /// Nazwa klienta do wyświetlenia, albo null gdy nieznana. <c>CustomerFullName</c> nie ma fallbacku
  /// (puste = brak nazwiska); <c>CustomerName</c> ma fallback na literał „Klient", który tu traktujemy
  /// jako „brak nazwy" (żeby nie robić „— Klient").
  /// </summary>
  private static string? CustomerLabel(NotificationPayload p)
  {
    if (!string.IsNullOrWhiteSpace(p.CustomerFullName))
    {
      return p.CustomerFullName;
    }

    if (!string.IsNullOrWhiteSpace(p.CustomerName) && p.CustomerName != "Klient")
    {
      return p.CustomerName;
    }

    return null;
  }

  /// <summary>Przyjazna data względem „dziś" salonu: dziś/jutro/wczoraj, dzień tygodnia w tym tygodniu, inaczej data.</summary>
  private static string? FriendlyWhen(DateOnly? date, TimeOnly? time, DateOnly today)
  {
    if (date is not { } d || time is not { } t)
    {
      return null;
    }

    var hm = t.ToString("HH:mm");
    var delta = d.DayNumber - today.DayNumber;

    return delta switch
    {
      0 => $"dziś {hm}",
      1 => $"jutro {hm}",
      -1 => $"wczoraj {hm}",
      >= 2 and <= 6 => $"{PolishWeekdayShort(d.DayOfWeek)} {d:dd.MM}, {hm}",
      _ => $"{d:dd.MM.yyyy}, {hm}",
    };
  }

  private static string PolishWeekdayShort(DayOfWeek dow) => dow switch
  {
    DayOfWeek.Monday => "pon.",
    DayOfWeek.Tuesday => "wt.",
    DayOfWeek.Wednesday => "śr.",
    DayOfWeek.Thursday => "czw.",
    DayOfWeek.Friday => "pt.",
    DayOfWeek.Saturday => "sob.",
    _ => "niedz.",
  };
}
