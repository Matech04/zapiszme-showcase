using App.Domain.Common;

namespace App.Domain.Aggregates.ServiceAggregate;

/// <summary>
/// Zdjęcie galerii usługi. Należy do agregatu <see cref="Service"/> jako kolekcja owned
/// (analogicznie do <see cref="ServiceAddon"/>). Plik leży w storage (R2/MinIO) — encja trzyma
/// tylko publiczne URL-e (<see cref="Url"/> + <see cref="ThumbnailUrl"/>) oraz <see cref="StorageKey"/>
/// klucza obiektu (do best-effort kasowania pliku przy usunięciu zdjęcia z galerii).
///
/// Upload pliku robi front PRZED zapisem usługi (POST /api/media/images) — domena dostaje już
/// gotowe URL-e + klucz, nie surowy bajt. Pierwsze zdjęcie wg <see cref="OrderIndex"/> = cover.
/// </summary>
public class ServiceImage : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid ServiceId { get; private set; }
  public string Url { get; private set; } = string.Empty;
  public string ThumbnailUrl { get; private set; } = string.Empty;
  public string StorageKey { get; private set; } = string.Empty;
  public int OrderIndex { get; private set; }

  internal ServiceImage(Guid tenantId, Guid serviceId, string url, string thumbnailUrl, string storageKey, int orderIndex)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    ServiceId = serviceId;
    Url = Guard.NormalizeRequiredText(url, nameof(url));
    ThumbnailUrl = Guard.NormalizeRequiredText(thumbnailUrl, nameof(thumbnailUrl));
    StorageKey = Guard.NormalizeRequiredText(storageKey, nameof(storageKey));
    OrderIndex = orderIndex;
  }

  private ServiceImage() { }

  internal void SetOrder(int orderIndex) => OrderIndex = orderIndex;
}

/// <summary>
/// Lekki opis zdjęcia przekazywany do <see cref="Service.SetImages"/> — front uploaduje plik wcześniej
/// (POST /api/media/images) i podaje gotowe URL-e + klucz storage. Kolejność na liście = kolejność galerii.
/// </summary>
public readonly record struct ServiceImageData(string Url, string ThumbnailUrl, string StorageKey);
