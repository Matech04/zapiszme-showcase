using App.Application.Common.Email;
using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Infrastructure.Notifications.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Rozsyła powiadomienie do wszystkich zarejestrowanych kanałów. Każdy kanał jest izolowany
/// try/catch — awaria jednego (np. SMTP) nie blokuje pozostałych.
///
/// Dla demo-tenantów (<c>Tenant.IsDemo</c>) wyciszamy kanały wychodzące (e-mail / SMS) —
/// dane demo (klientki @demo.local, salon demo) nie mogą generować realnego ruchu mailowego
/// ani płatnych SMS-ów. Kanał in-app zostaje, żeby tester widział powiadomienia w panelu.
/// To jeden centralny chokepoint pokrywający wszystkie typy powiadomień.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
  // Twardy timeout per kanał. Wysyłka leci synchronicznie w wątku żądania (np. po VerifyOtp),
  // a ACS/smsapi mogą się zawiesić — bez tego jeden wolny dostawca wysyca pulę wątków API
  // (single-instance failure mode). Po przekroczeniu: log + przejście do kolejnego kanału.
  private static readonly TimeSpan ChannelTimeout = TimeSpan.FromSeconds(15);

  private readonly IEnumerable<INotificationChannel> _channels;
  private readonly IApplicationDbContext _context;
  private readonly ILogger<NotificationDispatcher> _logger;

  public NotificationDispatcher(
    IEnumerable<INotificationChannel> channels,
    IApplicationDbContext context,
    ILogger<NotificationDispatcher> logger)
  {
    _channels = channels;
    _context = context;
    _logger = logger;
  }

  public async Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct)
  {
    var tenant = await _context.Tenants
      .AsNoTracking()
      .IgnoreQueryFilters()
      .Where(t => t.Id == message.TenantId)
      .Select(t => new { t.IsDemo, t.Name, t.BookingCalendarColorHex })
      .FirstOrDefaultAsync(ct);

    var isDemoTenant = tenant?.IsDemo ?? false;

    // Marka salonu dla kanału e-mail — tu, bo to jedyne miejsce w ścieżce wysyłki, które ma
    // i TenantId, i DbContext. Kanał jest singletonem od strony transportu, a przypomnienia lecą
    // z hosted service bez HttpContext, więc ICurrentTenantService nie jest tu wiarygodne.
    if (tenant is not null)
    {
      message = message with { Brand = EmailBrand.ForTenant(tenant.Name, tenant.BookingCalendarColorHex) };
    }

    var outcomes = new List<NotificationChannelOutcome>();

    foreach (var channel in _channels)
    {
      // Demo: tylko in-app. Pomijamy e-mail i SMS (realny ruch wychodzący / koszt).
      if (isDemoTenant && channel.Kind != NotificationChannelKind.InApp)
      {
        _logger.LogDebug(
          "Demo-tenant {TenantId}: pomijam kanał {Channel} dla powiadomienia {Type}.",
          message.TenantId,
          channel.Kind,
          message.Type);
        outcomes.Add(new(channel.Kind, NotificationDeliveryStatus.Suppressed, "demo-tenant"));
        continue;
      }

      try
      {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ChannelTimeout);
        var status = await channel.SendAsync(message, timeoutCts.Token);
        outcomes.Add(new(channel.Kind, status, status == NotificationDeliveryStatus.Sent ? null : "kanał pominął odbiorcę"));
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        // Nasz timeout (nie anulowanie żądania przez callera) — log i jedziemy dalej.
        _logger.LogWarning(
          "Kanał {Channel} przekroczył limit czasu {TimeoutSeconds}s dla powiadomienia {Type}.",
          channel.Kind,
          ChannelTimeout.TotalSeconds,
          message.Type);
        outcomes.Add(new(channel.Kind, NotificationDeliveryStatus.TimedOut, "timeout kanału"));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Nie przekazujemy obiektu wyjątku do loggera: SensitiveDataMaskingEnricher maskuje właściwości
        // zdarzenia, ale NIE dotyka LogEvent.Exception, a Message od smsapi/SMTP potrafi nieść numer
        // telefonu albo adres odbiorcy. Do diagnostyki wystarczy typ + kod błędu dostawcy.
        _logger.LogWarning(
          "Kanał {Channel} nie dostarczył powiadomienia {Type}: {ExceptionType} {ProviderCode}.",
          channel.Kind,
          message.Type,
          ex.GetType().Name,
          ex is SmsApiException sms ? sms.ErrorCode.ToString() : "-");

        outcomes.Add(new(channel.Kind, NotificationDeliveryStatus.Failed, ex.GetType().Name));
      }
    }

    return new NotificationDispatchResult(outcomes);
  }
}
