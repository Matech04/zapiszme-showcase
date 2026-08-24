using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Domain.Aggregates.NotificationAggregate;

/// <summary>
/// Wpis dziennika wysyłki wychodzącej (SMS / e-mail). Append-only — jeden wiersz na próbę wysyłki,
/// zasila panel „Zużycie SMS / e-mail" w danym miesiącu. Dla SMS przechowujemy <see cref="Points"/>
/// (koszt z odpowiedzi smsapi.pl); dla e-maila punktów nie ma (0). Numer/adres odbiorcy nie jest
/// zapisywany — do raportu wystarczy kanał, typ, czas i koszt.
/// </summary>
public class NotificationUsageRecord : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }

  public NotificationDeliveryChannel Channel { get; private set; }

  public NotificationType Type { get; private set; }

  /// <summary>Czas zarejestrowania wysyłki (UTC). Po tym polu agregujemy miesięcznie.</summary>
  public DateTime SentAtUtc { get; private set; }

  /// <summary>Czy dostawca przyjął wysyłkę (false = błąd kanału — liczymy do statystyk, ale bez kosztu).</summary>
  public bool Success { get; private set; }

  /// <summary>Koszt w punktach smsapi.pl (SMS). Dla e-maila i nieudanych wysyłek = 0.</summary>
  public decimal Points { get; private set; }

  private NotificationUsageRecord() { }

  public static NotificationUsageRecord ForSms(
    Guid tenantId,
    NotificationType type,
    bool success,
    decimal points,
    DateTime nowUtc) => new()
  {
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    Channel = NotificationDeliveryChannel.Sms,
    Type = type,
    Success = success,
    Points = success ? points : 0m,
    SentAtUtc = nowUtc,
  };

  public static NotificationUsageRecord ForEmail(
    Guid tenantId,
    NotificationType type,
    bool success,
    DateTime nowUtc) => new()
  {
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    Channel = NotificationDeliveryChannel.Email,
    Type = type,
    Success = success,
    Points = 0m,
    SentAtUtc = nowUtc,
  };
}
