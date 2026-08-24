using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.NotificationAggregate;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Kanał powiadomień in-app: zapisuje trwałe powiadomienie do tabeli <c>Notifications</c>
/// i pushuje je w czasie rzeczywistym do dashboardu salonu przez <see cref="IRealtimeNotifier"/>.
/// Wiadomości dla klienta końcowego (<c>Recipient.UserId == null</c>) są pomijane — klient nie
/// ma panelu. Best-effort jest zapewniony przez per-kanał try/catch w <c>NotificationDispatcher</c>.
/// </summary>
public sealed class InAppNotificationChannel : INotificationChannel
{
  private readonly IApplicationDbContext _context;
  private readonly IRealtimeNotifier _realtime;
  private readonly ILogger<InAppNotificationChannel> _logger;

  public InAppNotificationChannel(
    IApplicationDbContext context,
    IRealtimeNotifier realtime,
    ILogger<InAppNotificationChannel> logger)
  {
    _context = context;
    _realtime = realtime;
    _logger = logger;
  }

  public NotificationChannelKind Kind => NotificationChannelKind.InApp;

  public async Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
  {
    if (message.Recipient.UserId is null)
    {
      _logger.LogDebug(
        "Pomijam in-app dla {Type} — odbiorca bez konta panelu (wiadomość dla klienta).",
        message.Type);
      return NotificationDeliveryStatus.Skipped;
    }

    var entity = Notification.Create(
      message.TenantId,
      message.Recipient.UserId,
      message.Type,
      message.Subject,
      message.ShortText,
      message.ReferenceId,
      DateTime.UtcNow);

    // Osobny SaveChanges od oryginalnej komendy — ta jest już scommitowana (zdarzenie
    // publikowane po SaveChangesAsync). Handlery zdarzeń czytają przez AsNoTracking, więc
    // tracker jest czysty poza dodanym Notification. Guard tenanta w ApplicationDbContext
    // przechodzi: message.TenantId == rozwiązany tenant żądania.
    _context.Notifications.Add(entity);
    await _context.SaveChangesAsync(ct);

    var dto = new RealtimeNotificationDto(
      entity.Id,
      entity.TenantId,
      (int)entity.Type,
      entity.Subject,
      entity.ShortText,
      entity.ReferenceId,
      entity.CreatedAtUtc,
      Read: false);

    await _realtime.NotifyRecipientAsync(message.TenantId, message.Recipient.UserId, dto, ct);
    return NotificationDeliveryStatus.Sent;
  }
}
