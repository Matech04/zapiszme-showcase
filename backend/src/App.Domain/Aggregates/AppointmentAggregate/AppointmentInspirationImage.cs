using App.Domain.Common;

namespace App.Domain.Aggregates.AppointmentAggregate;

/// <summary>
/// Zdjęcie inspiracji dołączone przez klientkę przy publicznej rezerwacji (np. fryzura, paznokcie).
/// Obraz jest już przetworzony (EXIF strip, WebP + miniatura) i wgrany do storage przez anonimowy
/// endpoint uploadu — tu trzymamy wyłącznie publiczne URL-e i klucz obiektu w buckecie. Klucz
/// (<see cref="StorageKey"/>) generuje serwer (pipeline) losowo z prefiksem <c>inspirations/</c> —
/// NIGDY user-controlled (path traversal). Owned collection na <see cref="Appointment"/> — kasowana
/// kaskadowo razem z wizytą (child wizyty). Pracownik widzi miniatury w panelu szczegółów wizyty.
/// </summary>
public class AppointmentInspirationImage : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid AppointmentId { get; private set; }

  /// <summary>Publiczny URL głównego obrazu (WebP).</summary>
  public string Url { get; private set; } = string.Empty;

  /// <summary>Publiczny URL miniatury (WebP).</summary>
  public string ThumbnailUrl { get; private set; } = string.Empty;

  /// <summary>Klucz obiektu głównego w storage (np. <c>inspirations/{guid}.webp</c>) — do usunięcia z R2.</summary>
  public string StorageKey { get; private set; } = string.Empty;

  /// <summary>
  /// Klucz obiektu miniatury w storage (osobny obiekt, np. <c>inspirations/{guid}_thumb.webp</c>). Musi
  /// być persystowany odrębnie — nie da się go wyprowadzić z klucza głównego — żeby przy kasowaniu (np.
  /// wizyta terminalna) usunąć z R2 oba obiekty, nie zostawiając miniatury sierotą.
  /// </summary>
  public string ThumbnailStorageKey { get; private set; } = string.Empty;

  public AppointmentInspirationImage(Guid tenantId, Guid appointmentId, string url, string thumbnailUrl, string storageKey, string thumbnailStorageKey)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    AppointmentId = appointmentId;
    Url = url;
    ThumbnailUrl = thumbnailUrl;
    StorageKey = storageKey;
    ThumbnailStorageKey = thumbnailStorageKey;
  }

  private AppointmentInspirationImage() { }
}

/// <summary>
/// Wejściowa pozycja inspiracji przekazywana przez publiczny booking-flow przy tworzeniu holdu —
/// (URL, miniatura, klucz) zwrócone wcześniej przez anonimowy endpoint uploadu. Aggregate buduje
/// z niej <see cref="AppointmentInspirationImage"/>.
/// </summary>
public readonly record struct AppointmentInspirationLine(string Url, string ThumbnailUrl, string StorageKey, string ThumbnailStorageKey);
