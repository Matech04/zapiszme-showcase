using App.Domain.Common;

namespace App.Domain.Aggregates.NotificationAggregate;

/// <summary>
/// Subskrypcja Web Push pojedynczego urządzenia/przeglądarki pracownika panelu. Powstaje, gdy
/// zalogowany użytkownik włączy powiadomienia na danym urządzeniu (<c>SwPush.requestSubscription</c>).
/// Kanał <c>WebPushNotificationChannel</c> wysyła powiadomienia na wszystkie aktywne subskrypcje
/// odbiorcy (<see cref="UserId"/>). Endpoint jest globalnie unikalny (nadaje go push-service
/// przeglądarki) — przy ponownej subskrypcji tego samego endpointu robimy upsert.
/// Martwe subskrypcje (404/410 od push-service) kanał kasuje.
/// </summary>
public class PushSubscription : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }

  /// <summary>Identity user pracownika, do którego należy to urządzenie.</summary>
  public Guid UserId { get; private set; }

  /// <summary>URL push-service przeglądarki (globalnie unikalny identyfikator subskrypcji).</summary>
  public string Endpoint { get; private set; } = string.Empty;

  /// <summary>Klucz publiczny klienta (P-256 ECDH, base64url) — do szyfrowania payloadu.</summary>
  public string P256dh { get; private set; } = string.Empty;

  /// <summary>Sekret uwierzytelniający subskrypcji (base64url).</summary>
  public string Auth { get; private set; } = string.Empty;

  public DateTime CreatedAtUtc { get; private set; }

  private PushSubscription() { }

  public static PushSubscription Create(
    Guid tenantId,
    Guid userId,
    string endpoint,
    string p256dh,
    string auth,
    DateTime nowUtc) => new()
  {
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    UserId = userId,
    Endpoint = endpoint,
    P256dh = p256dh,
    Auth = auth,
    CreatedAtUtc = nowUtc,
  };

  /// <summary>Aktualizuje klucze i właściciela przy ponownej subskrypcji tego samego endpointu.</summary>
  public void Refresh(Guid userId, string p256dh, string auth, DateTime nowUtc)
  {
    UserId = userId;
    P256dh = p256dh;
    Auth = auth;
    CreatedAtUtc = nowUtc;
  }
}
