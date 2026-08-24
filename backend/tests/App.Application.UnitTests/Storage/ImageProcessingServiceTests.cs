using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using App.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace App.Application.UnitTests.Storage;

/// <summary>
/// STORAGE-IMG-* — pipeline obróbki obrazu: magic-bytes (akceptacja realnych obrazów, odrzucenie
/// nie-obrazów), limit rozmiaru, strip EXIF, downscale, WebP główny + miniatura, klucze losowe Guid.
/// Storage zmockowany prostym in-memory fake (bez MinIO/S3).
/// </summary>
public sealed class ImageProcessingServiceTests
{
  private static ImageProcessingService CreateSut(FakeFileStorage storage, ImageProcessingOptions? opts = null) =>
    new(
      storage,
      Options.Create(opts ?? new ImageProcessingOptions()),
      NullLogger<ImageProcessingService>.Instance);

  private static byte[] CreateJpeg(int width, int height)
  {
    using var image = new Image<Rgba32>(width, height);
    using var ms = new MemoryStream();
    image.Save(ms, new JpegEncoder());
    return ms.ToArray();
  }

  private static byte[] CreatePng(int width, int height)
  {
    using var image = new Image<Rgba32>(width, height);
    using var ms = new MemoryStream();
    image.Save(ms, new PngEncoder());
    return ms.ToArray();
  }

  [Fact]
  public async Task ProcessAndStore_ValidJpeg_StoresMainAndThumbnailAsWebp()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    using var input = new MemoryStream(CreateJpeg(800, 600));
    var result = await sut.ProcessAndStoreAsync(input, "services", TestContext.Current.CancellationToken);

    // Dwa obiekty: główny + miniatura, oba WebP.
    Assert.Equal(2, storage.Uploads.Count);
    Assert.All(storage.Uploads, u => Assert.Equal("image/webp", u.ContentType));
    Assert.EndsWith(".webp", result.Key);
    Assert.Contains("services/", result.Key);
    Assert.StartsWith("https://cdn.test/", result.Url);
    Assert.StartsWith("https://cdn.test/", result.ThumbnailUrl);

    // Główny obraz to faktyczny WebP (sygnatura RIFF....WEBP).
    var mainBytes = storage.Uploads[0].Bytes;
    Assert.True(mainBytes.Length > 12);
    Assert.Equal((byte)'R', mainBytes[0]);
    Assert.Equal((byte)'W', mainBytes[8]);
  }

  [Fact]
  public async Task ProcessAndStore_ValidPng_IsAccepted()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    using var input = new MemoryStream(CreatePng(300, 300));
    var result = await sut.ProcessAndStoreAsync(input, "inspirations", TestContext.Current.CancellationToken);

    Assert.Equal(2, storage.Uploads.Count);
    Assert.Contains("inspirations/", result.Key);
  }

  [Fact]
  public async Task ProcessAndStore_NonImageBytes_ThrowsInvalidImage()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    // Zwykły tekst — nie obraz, choć ma >12 bajtów. Magic-bytes muszą to odrzucić.
    using var input = new MemoryStream("this is definitely not an image file"u8.ToArray());

    var ex = await Assert.ThrowsAsync<InvalidImageException>(
      () => sut.ProcessAndStoreAsync(input, "services", TestContext.Current.CancellationToken));
    Assert.Equal(ErrorCodes.ImageUnsupportedFormat, ex.ErrorCode);
    Assert.Empty(storage.Uploads);
  }

  [Fact]
  public async Task ProcessAndStore_HeicMagicBytes_RejectedWithUnsupportedFormat()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    // Nagłówek ISO-BMFF: [size][ftyp][heic]...
    var heic = new byte[]
    {
      0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
      (byte)'h', (byte)'e', (byte)'i', (byte)'c',
    };
    using var input = new MemoryStream(heic);

    var ex = await Assert.ThrowsAsync<InvalidImageException>(
      () => sut.ProcessAndStoreAsync(input, "services", TestContext.Current.CancellationToken));
    Assert.Equal(ErrorCodes.ImageUnsupportedFormat, ex.ErrorCode);
    Assert.Empty(storage.Uploads);
  }

  [Fact]
  public async Task ProcessAndStore_OversizedInput_ThrowsTooLarge()
  {
    var storage = new FakeFileStorage();
    // Limit 1 KB — łatwo przekroczyć zwykłym JPEG.
    var sut = CreateSut(storage, new ImageProcessingOptions { MaxInputBytes = 1024 });

    using var input = new MemoryStream(CreateJpeg(800, 600));

    var ex = await Assert.ThrowsAsync<InvalidImageException>(
      () => sut.ProcessAndStoreAsync(input, "services", TestContext.Current.CancellationToken));
    Assert.Equal(ErrorCodes.ImageTooLarge, ex.ErrorCode);
    Assert.Empty(storage.Uploads);
  }

  [Fact]
  public async Task ProcessAndStore_LargeImage_DownscaledToMaxDimension()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage, new ImageProcessingOptions { MaxDimension = 100, ThumbnailDimension = 40 });

    using var input = new MemoryStream(CreatePng(500, 250));
    await sut.ProcessAndStoreAsync(input, "services", TestContext.Current.CancellationToken);

    // Główny: dłuższy bok <= 100.
    using var mainOut = Image.Load(storage.Uploads[0].Bytes);
    Assert.True(mainOut.Width <= 100 && mainOut.Height <= 100);
    Assert.Equal(100, mainOut.Width); // 500x250 → max=100 → 100x50

    // Miniatura: dłuższy bok <= 40.
    using var thumbOut = Image.Load(storage.Uploads[1].Bytes);
    Assert.True(thumbOut.Width <= 40 && thumbOut.Height <= 40);
  }

  [Fact]
  public async Task ProcessAndStore_StripsExifMetadata()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    // Obraz z profilem EXIF (geolokacja / model urządzenia w realnym świecie).
    using var src = new Image<Rgba32>(200, 200);
    src.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
    src.Metadata.ExifProfile.SetValue(
      SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, "TestCam");
    using var inputMs = new MemoryStream();
    src.Save(inputMs, new JpegEncoder());
    inputMs.Position = 0;

    await sut.ProcessAndStoreAsync(inputMs, "services", TestContext.Current.CancellationToken);

    using var outImage = Image.Load(storage.Uploads[0].Bytes);
    Assert.Null(outImage.Metadata.ExifProfile);
  }

  [Fact]
  public async Task ProcessAndStore_GeneratesRandomKeys_NotDerivedFromInput()
  {
    var storage = new FakeFileStorage();
    var sut = CreateSut(storage);

    using var input1 = new MemoryStream(CreateJpeg(100, 100));
    using var input2 = new MemoryStream(CreateJpeg(100, 100));
    var r1 = await sut.ProcessAndStoreAsync(input1, "services", TestContext.Current.CancellationToken);
    var r2 = await sut.ProcessAndStoreAsync(input2, "services", TestContext.Current.CancellationToken);

    // Identyczne wejście → różne klucze (Guid), brak kolizji/nadpisania.
    Assert.NotEqual(r1.Key, r2.Key);
  }

  private sealed class FakeFileStorage : IFileStorage
  {
    public List<(string Key, string ContentType, byte[] Bytes)> Uploads { get; } = [];

    public async Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default)
    {
      using var ms = new MemoryStream();
      await content.CopyToAsync(ms, ct);
      Uploads.Add((key, contentType, ms.ToArray()));
      return BuildPublicUrl(key);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public string BuildPublicUrl(string key) => $"https://cdn.test/{key}";
  }
}
