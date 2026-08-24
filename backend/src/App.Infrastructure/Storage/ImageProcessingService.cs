using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace App.Infrastructure.Storage;

/// <summary>
/// Pipeline obróbki obrazu: walidacja magic-bytes → wczytanie przez ImageSharp → zdjęcie EXIF
/// (prywatność: geolokacja/urządzenie) → ograniczenie wymiarów → enkodowanie głównego obrazu WebP
/// + miniatury WebP → upload obu przez <see cref="IFileStorage"/>.
///
/// Walidacja formatu opiera się o MAGIC BYTES (sygnaturę pliku), NIE o content-type/rozszerzenie
/// (te są user-controlled i łatwe do podrobienia). ImageSharp wykrywa format po nagłówku;
/// nieobsługiwany/nie-obraz → <see cref="InvalidImageException"/>.
/// </summary>
public sealed class ImageProcessingService : IImageProcessingService
{
  // Magic bytes dozwolonych formatów wejściowych. HEIC jest kontenerem ISO-BMFF (ftyp) —
  // ImageSharp w domyślnej konfiguracji go NIE dekoduje, więc HEIC jawnie odrzucamy z czytelnym
  // komunikatem (zamiast generycznego "nieobsługiwany format"). #2/#5 mogą dołożyć dekoder HEIC.
  private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
  private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  private readonly IFileStorage _storage;
  private readonly ImageProcessingOptions _options;
  private readonly ILogger<ImageProcessingService> _logger;

  public ImageProcessingService(
    IFileStorage storage,
    IOptions<ImageProcessingOptions> options,
    ILogger<ImageProcessingService> logger)
  {
    _storage = storage;
    _options = options.Value;
    _logger = logger;
  }

  public async Task<ProcessedImageResult> ProcessAndStoreAsync(
    Stream content,
    string keyPrefix,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

    // 1. Wczytaj do pamięci z twardym limitem rozmiaru — czytamy max MaxInputBytes+1, żeby
    //    wykryć przekroczenie bez wciągania gigabajtów do RAM.
    using var buffer = new MemoryStream();
    await CopyWithLimitAsync(content, buffer, _options.MaxInputBytes, ct);
    buffer.Position = 0;

    // 2. Walidacja magic-bytes PRZED przekazaniem do dekodera.
    ValidateMagicBytes(buffer);
    buffer.Position = 0;

    // 3. Dekoduj. ImageSharp sam rozpoznaje format po nagłówku; nie-obraz rzuca tu wyjątek.
    Image image;
    try
    {
      image = await Image.LoadAsync(buffer, ct);
    }
    catch (UnknownImageFormatException)
    {
      throw new InvalidImageException(
        "Przesłany plik nie jest obsługiwanym obrazem.", ErrorCodes.ImageUnsupportedFormat);
    }
    catch (InvalidImageContentException ex)
    {
      _logger.LogWarning(ex, "Uszkodzona zawartość obrazu przy dekodowaniu.");
      throw new InvalidImageException("Plik obrazu jest uszkodzony lub niekompletny.");
    }

    using (image)
    {
      // 4. Zdejmij metadane (EXIF/IPTC/XMP) — prywatność: geolokacja, model urządzenia, miniatury.
      StripMetadata(image);

      // 5. Główny obraz: ogranicz dłuższy bok do MaxDimension.
      var mainKey = $"{keyPrefix.Trim('/')}/{Guid.NewGuid():N}.webp";
      var mainUrl = await EncodeResizeAndStoreAsync(
        image, mainKey, _options.MaxDimension, _options.WebpQuality, ct);

      // 6. Miniatura: osobna kopia przeskalowana do ThumbnailDimension (klon, by nie zniszczyć źródła).
      var thumbKey = $"{keyPrefix.Trim('/')}/{Guid.NewGuid():N}_thumb.webp";
      using var thumb = image.Clone(_ => { });
      var thumbUrl = await EncodeResizeAndStoreAsync(
        thumb, thumbKey, _options.ThumbnailDimension, _options.ThumbnailWebpQuality, ct);

      return new ProcessedImageResult(mainKey, mainUrl, thumbUrl, thumbKey);
    }
  }

  private async Task<string> EncodeResizeAndStoreAsync(
    Image image, string key, int maxDimension, int quality, CancellationToken ct)
  {
    image.Mutate(x => x.Resize(new ResizeOptions
    {
      // Max: skaluje w dół tak, by oba boki zmieściły się w kwadracie maxDimension; nie powiększa.
      Mode = ResizeMode.Max,
      Size = new Size(maxDimension, maxDimension),
    }));

    using var output = new MemoryStream();
    await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality }, ct);
    output.Position = 0;

    return await _storage.UploadAsync(output, key, "image/webp", ct);
  }

  private static void StripMetadata(Image image)
  {
    image.Metadata.ExifProfile = null;
    image.Metadata.IptcProfile = null;
    image.Metadata.XmpProfile = null;
    image.Metadata.IccProfile = null;
  }

  private void ValidateMagicBytes(Stream stream)
  {
    Span<byte> header = stackalloc byte[12];
    var read = stream.Read(header);
    if (read < 12)
    {
      throw new InvalidImageException("Plik jest zbyt mały, aby być prawidłowym obrazem.");
    }

    if (StartsWith(header, JpegMagic) || StartsWith(header, PngMagic) || IsWebp(header))
    {
      return;
    }

    if (IsHeic(header))
    {
      throw new InvalidImageException(
        "Format HEIC nie jest obecnie obsługiwany — prześlij JPEG, PNG lub WebP.",
        ErrorCodes.ImageUnsupportedFormat);
    }

    throw new InvalidImageException(
      "Przesłany plik nie jest obsługiwanym obrazem (dozwolone: JPEG, PNG, WebP).",
      ErrorCodes.ImageUnsupportedFormat);
  }

  // WebP: bajty 0-3 = "RIFF", bajty 8-11 = "WEBP".
  private static bool IsWebp(ReadOnlySpan<byte> header) =>
    header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
    && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';

  // HEIC/HEIF: ISO-BMFF — bajty 4-7 = "ftyp", marka (8-11) = heic/heix/mif1/msf1.
  private static bool IsHeic(ReadOnlySpan<byte> header)
  {
    if (!(header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p'))
    {
      return false;
    }

    var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
    return brand is "heic" or "heix" or "mif1" or "msf1" or "hevc" or "heim" or "heis";
  }

  private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
    data.Length >= prefix.Length && data.Slice(0, prefix.Length).SequenceEqual(prefix);

  private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
  {
    var bufferArray = new byte[81920];
    long total = 0;
    int read;
    while ((read = await source.ReadAsync(bufferArray, ct)) > 0)
    {
      total += read;
      if (total > maxBytes)
      {
        var mb = maxBytes / (1024.0 * 1024.0);
        throw new InvalidImageException(
          $"Obraz przekracza maksymalny rozmiar {mb:0.#} MB.", ErrorCodes.ImageTooLarge);
      }

      await destination.WriteAsync(bufferArray.AsMemory(0, read), ct);
    }
  }
}
