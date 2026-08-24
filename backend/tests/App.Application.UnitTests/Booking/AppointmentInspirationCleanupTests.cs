using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// AppointmentInspirationCleanup — sprzątanie zdjęć inspiracji z bazy i R2, gdy podgląd zbędny
/// (wizyta terminalna). Sprawdzamy: DetachImages zbiera OBA klucze (główny + miniatura) każdego zdjęcia
/// i czyści rekordy agregatu; PurgeStorageAsync kasuje wszystkie klucze; błąd kasowania jest best-effort.
/// </summary>
public sealed class AppointmentInspirationCleanupTests
{
  private static Appointment AppointmentWithImages(int count)
  {
    var appt = new Appointment(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 7, 1), new TimeOnly(10, 0), new TimeOnly(11, 0),
        AppointmentStatus.Completed, new Money(100m, "PLN"), "", null);

    var lines = Enumerable.Range(0, count)
        .Select(i => new AppointmentInspirationLine(
            $"https://cdn/{i}.webp", $"https://cdn/{i}_thumb.webp", $"inspirations/{i}.webp", $"inspirations/{i}_thumb.webp"))
        .ToList();
    appt.SetInspirationImages(lines);
    return appt;
  }

  private sealed class RecordingFileStorage : IFileStorage
  {
    public List<string> Deleted { get; } = new();
    public bool Throw { get; set; }
    public Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default)
        => Task.FromResult(key);
    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
      if (Throw)
      {
        throw new InvalidOperationException("storage down");
      }
      Deleted.Add(key);
      return Task.CompletedTask;
    }
    public string BuildPublicUrl(string key) => $"https://cdn.test/{key}";
  }

  private static AppointmentInspirationCleanup Create(RecordingFileStorage storage)
      => new(storage, NullLogger<AppointmentInspirationCleanup>.Instance);

  [Fact]
  public void DetachImages_collects_main_and_thumb_keys_and_clears_records()
  {
    var appt = AppointmentWithImages(2);
    var cleanup = Create(new RecordingFileStorage());

    var keys = cleanup.DetachImages(appt);

    // 2 zdjęcia × (główny + miniatura) = 4 klucze.
    Assert.Equal(4, keys.Count);
    Assert.Contains("inspirations/0.webp", keys);
    Assert.Contains("inspirations/0_thumb.webp", keys);
    Assert.Contains("inspirations/1_thumb.webp", keys);
    // Rekordy zdjęć zostały zdjęte z agregatu (skasują się przy SaveChanges).
    Assert.Empty(appt.InspirationImages);
  }

  [Fact]
  public void DetachImages_with_no_images_returns_empty()
  {
    var appt = AppointmentWithImages(0);
    var cleanup = Create(new RecordingFileStorage());

    Assert.Empty(cleanup.DetachImages(appt));
  }

  [Fact]
  public async Task PurgeStorageAsync_deletes_every_key()
  {
    var appt = AppointmentWithImages(3);
    var storage = new RecordingFileStorage();
    var cleanup = Create(storage);

    var keys = cleanup.DetachImages(appt);
    await cleanup.PurgeStorageAsync(keys, CancellationToken.None);

    Assert.Equal(6, storage.Deleted.Count); // 3 zdjęcia × 2 obiekty
    Assert.Contains("inspirations/2_thumb.webp", storage.Deleted);
  }

  [Fact]
  public async Task PurgeStorageAsync_is_best_effort_when_storage_throws()
  {
    var appt = AppointmentWithImages(1);
    var storage = new RecordingFileStorage { Throw = true };
    var cleanup = Create(storage);

    var keys = cleanup.DetachImages(appt);

    // Błąd kasowania ze storage NIE może wywrócić operacji (wizyta jest już terminalna).
    await cleanup.PurgeStorageAsync(keys, CancellationToken.None);
  }
}
