using App.Application.Common.Email;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications;

/// <summary>
/// Gotowa do wysłania jednostka powiadomienia: dla kogo, jaki typ, oraz dane do wyrenderowania
/// treści. Tworzona przez handlery zdarzeń, rozsyłana przez <see cref="INotificationDispatcher"/>
/// do wszystkich kanałów.
/// </summary>
/// <param name="Brand">
/// Marka salonu do wyglądu maila. Handlery jej nie ustawiają — dopisuje ją dispatcher, który
/// i tak czyta encję Tenant. Null oznacza „nieznany tenant" i kanał e-mail spada na markę domyślną.
/// </param>
public record NotificationMessage(
  Guid TenantId,
  NotificationType Type,
  NotificationRecipient Recipient,
  string Subject,
  string ShortText,
  NotificationPayload Payload,
  Guid? ReferenceId = null,
  EmailBrand? Brand = null);
