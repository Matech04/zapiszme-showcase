using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using Microsoft.Extensions.Logging;

namespace App.Application.Common;

/// <summary>
/// Sprząta zdjęcia inspiracji wizyty z bazy ORAZ ze storage (R2), gdy podgląd przestaje być potrzebny —
/// np. po przejściu wizyty w stan terminalny (Completed/Canceled). Dwufazowo, by zachować poprawną
/// kolejność: najpierw zdejmujemy rekordy z agregatu (commit razem ze zmianą statusu), a obiekty z
/// bucketa kasujemy DOPIERO po udanym zapisie (best-effort) — jak we wzorcu z UpdateService.
/// </summary>
public interface IAppointmentInspirationCleanup
{
  /// <summary>
  /// Zdejmuje rekordy zdjęć inspiracji z agregatu (woła <see cref="Appointment.ClearInspirationImages"/>)
  /// i zwraca klucze storage do skasowania PO commit (główny + miniatura każdego zdjęcia). Pusta lista,
  /// gdy wizyta nie ma zdjęć. Wywołujący MUSI zapisać zmiany (SaveChanges).
  /// </summary>
  IReadOnlyList<string> DetachImages(Appointment appointment);

  /// <summary>
  /// Best-effort usunięcie obiektów ze storage. Wołać PO udanym SaveChanges. Nigdy nie rzuca (poza
  /// anulowaniem) — błąd kasowania nie może wywrócić zatwierdzonej zmiany statusu wizyty.
  /// </summary>
  Task PurgeStorageAsync(IReadOnlyList<string> keys, CancellationToken ct);
}

internal sealed class AppointmentInspirationCleanup : IAppointmentInspirationCleanup
{
  private readonly IFileStorage _storage;
  private readonly ILogger<AppointmentInspirationCleanup> _logger;

  public AppointmentInspirationCleanup(IFileStorage storage, ILogger<AppointmentInspirationCleanup> logger)
  {
    _storage = storage;
    _logger = logger;
  }

  public IReadOnlyList<string> DetachImages(Appointment appointment)
  {
    if (appointment.InspirationImages.Count == 0)
    {
      return Array.Empty<string>();
    }

    // Każde zdjęcie to DWA niezależne obiekty w R2 (główny + miniatura) — oba mają osobny klucz.
    var keys = appointment.InspirationImages
        .SelectMany(i => new[] { i.StorageKey, i.ThumbnailStorageKey })
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .ToList();

    appointment.ClearInspirationImages();
    return keys;
  }

  public async Task PurgeStorageAsync(IReadOnlyList<string> keys, CancellationToken ct)
  {
    foreach (var key in keys)
    {
      try
      {
        await _storage.DeleteAsync(key, ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogWarning(ex,
            "Nie udało się usunąć zdjęcia inspiracji ze storage (klucz: {StorageKey}).", key);
      }
    }
  }
}
