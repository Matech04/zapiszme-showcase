using App.Application.Common;
using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Application.UnitTests.TestSupport;

/// <summary>
/// Testowy <see cref="IAppointmentInspirationCleanup"/> — wiernie odwzorowuje zachowanie (zdejmuje
/// rekordy z agregatu + zbiera klucze main+thumb), ale zamiast kasować z R2 zapisuje klucze do
/// <see cref="Purged"/>, żeby test mógł zweryfikować, że czyszczenie zaszło.
/// </summary>
internal sealed class RecordingInspirationCleanup : IAppointmentInspirationCleanup
{
  public List<string> Detached { get; } = new();
  public List<string> Purged { get; } = new();

  public IReadOnlyList<string> DetachImages(Appointment appointment)
  {
    var keys = appointment.InspirationImages
        .SelectMany(i => new[] { i.StorageKey, i.ThumbnailStorageKey })
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .ToList();
    appointment.ClearInspirationImages();
    Detached.AddRange(keys);
    return keys;
  }

  public Task PurgeStorageAsync(IReadOnlyList<string> keys, CancellationToken ct)
  {
    Purged.AddRange(keys);
    return Task.CompletedTask;
  }
}
