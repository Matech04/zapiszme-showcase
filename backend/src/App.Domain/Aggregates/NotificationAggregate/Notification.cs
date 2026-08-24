using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Domain.Aggregates.NotificationAggregate;

/// <summary>
/// Trwałe powiadomienie in-app dla salonu. Zasila dzwonek w panelu i jest źródłem prawdy
/// stanu „przeczytane". Tworzone przez kanał in-app przy obsłudze zdarzenia powiadomienia.
/// </summary>
public class Notification : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }

  /// <summary>User powiązanego pracownika (np. wizyta jego dotyczy). Nullable; dzwonek jest tenant-wide w v1.</summary>
  public Guid? RecipientUserId { get; private set; }

  public NotificationType Type { get; private set; }

  public string Subject { get; private set; } = string.Empty;
  public string ShortText { get; private set; } = string.Empty;

  /// <summary>Id powiązanego obiektu (np. wizyty) — do deep-linku z dzwonka.</summary>
  public Guid? ReferenceId { get; private set; }

  public DateTime CreatedAtUtc { get; private set; }

  /// <summary>Null = nieprzeczytane.</summary>
  public DateTime? ReadAtUtc { get; private set; }

  private Notification() { }

  public static Notification Create(
    Guid tenantId,
    Guid? recipientUserId,
    NotificationType type,
    string subject,
    string shortText,
    Guid? referenceId,
    DateTime nowUtc)
  {
    return new Notification
    {
      Id = Guid.NewGuid(),
      TenantId = tenantId,
      RecipientUserId = recipientUserId,
      Type = type,
      Subject = subject,
      ShortText = shortText,
      ReferenceId = referenceId,
      CreatedAtUtc = nowUtc,
      ReadAtUtc = null,
    };
  }

  public void MarkRead(DateTime nowUtc)
  {
    ReadAtUtc ??= nowUtc;
  }
}
