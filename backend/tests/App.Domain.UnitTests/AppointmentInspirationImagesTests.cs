using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

/// <summary>Inwarianty zdjęć inspiracji wizyty: cap (≤3), zastąpienie, brak/puste = no-op.</summary>
public class AppointmentInspirationImagesTests
{
  private static Appointment Create()
    => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 6, 10), new TimeOnly(10, 0), new TimeOnly(11, 0),
        AppointmentStatus.AwaitingOtp, new Money(100m, "PLN"), "", null);

  private static AppointmentInspirationLine Img(string id)
    => new($"https://cdn/inspirations/{id}.webp", $"https://cdn/inspirations/{id}_thumb.webp", $"inspirations/{id}.webp", $"inspirations/{id}_thumb.webp");

  [Fact]
  public void Sets_inspiration_images_up_to_cap()
  {
    var appt = Create();

    appt.SetInspirationImages(new[] { Img("a"), Img("b"), Img("c") });

    Assert.Equal(3, appt.InspirationImages.Count);
    Assert.Contains(appt.InspirationImages, i => i.StorageKey == "inspirations/a.webp");
    Assert.All(appt.InspirationImages, i => Assert.Equal(appt.TenantId, i.TenantId));
    Assert.All(appt.InspirationImages, i => Assert.Equal(appt.Id, i.AppointmentId));
  }

  [Fact]
  public void Rejects_more_than_max_inspiration_images()
  {
    var appt = Create();
    var tooMany = Enumerable.Range(0, Appointment.MaxInspirationImages + 1)
        .Select(i => Img(i.ToString()))
        .ToArray();

    var ex = Assert.Throws<AppointmentBookingRuleException>(() => appt.SetInspirationImages(tooMany));
    Assert.Equal(ErrorCodes.AppointmentTooManyInspirationImages, ex.ErrorCode);
    Assert.Empty(appt.InspirationImages);
  }

  [Fact]
  public void Empty_or_null_clears_images()
  {
    var appt = Create();
    appt.SetInspirationImages(new[] { Img("a") });
    Assert.Single(appt.InspirationImages);

    appt.SetInspirationImages(Array.Empty<AppointmentInspirationLine>());
    Assert.Empty(appt.InspirationImages);
  }

  [Fact]
  public void Setting_again_replaces_previous_images()
  {
    var appt = Create();
    appt.SetInspirationImages(new[] { Img("a"), Img("b") });

    appt.SetInspirationImages(new[] { Img("c") });

    Assert.Single(appt.InspirationImages);
    Assert.Equal("inspirations/c.webp", appt.InspirationImages.First().StorageKey);
  }

  [Fact]
  public void Add_inspiration_image_appends_up_to_cap()
  {
    var appt = Create();

    appt.AddInspirationImage(Img("a"));
    appt.AddInspirationImage(Img("b"));
    appt.AddInspirationImage(Img("c"));

    Assert.Equal(3, appt.InspirationImages.Count);
    Assert.All(appt.InspirationImages, i => Assert.Equal(appt.TenantId, i.TenantId));
    Assert.All(appt.InspirationImages, i => Assert.Equal(appt.Id, i.AppointmentId));
  }

  [Fact]
  public void Add_inspiration_image_beyond_cap_throws_and_keeps_previous()
  {
    var appt = Create();
    appt.AddInspirationImage(Img("a"));
    appt.AddInspirationImage(Img("b"));
    appt.AddInspirationImage(Img("c"));

    var ex = Assert.Throws<AppointmentBookingRuleException>(() => appt.AddInspirationImage(Img("d")));
    Assert.Equal(ErrorCodes.AppointmentTooManyInspirationImages, ex.ErrorCode);
    Assert.Equal(3, appt.InspirationImages.Count);
  }

  [Fact]
  public void Add_inspiration_image_persists_main_and_thumbnail_storage_keys()
  {
    var appt = Create();

    appt.AddInspirationImage(Img("a"));

    var image = appt.InspirationImages.Single();
    Assert.Equal("inspirations/a.webp", image.StorageKey);
    // Klucz miniatury MUSI być osobno persystowany — inaczej miniatura zostaje sierotą w R2.
    Assert.Equal("inspirations/a_thumb.webp", image.ThumbnailStorageKey);
  }

  [Fact]
  public void Clear_inspiration_images_removes_all()
  {
    var appt = Create();
    appt.AddInspirationImage(Img("a"));
    appt.AddInspirationImage(Img("b"));

    appt.ClearInspirationImages();

    Assert.Empty(appt.InspirationImages);
  }
}
