namespace App.Infrastructure.Storage;

/// <summary>
/// Limity i parametry pipeline'u obróbki obrazu (sekcja <c>Storage:ImageProcessing</c>).
/// </summary>
public sealed class ImageProcessingOptions
{
  public const string SectionName = "Storage:ImageProcessing";

  /// <summary>Twardy limit rozmiaru pojedynczego wejściowego obrazu w bajtach. Default 5 MB.</summary>
  public long MaxInputBytes { get; set; } = 5 * 1024 * 1024;

  /// <summary>Maks. długość dłuższego boku głównego obrazu (px). Większe są skalowane w dół. Default 2000.</summary>
  public int MaxDimension { get; set; } = 2000;

  /// <summary>Maks. długość dłuższego boku miniatury (px). Default 400.</summary>
  public int ThumbnailDimension { get; set; } = 400;

  /// <summary>Jakość WebP głównego obrazu (1-100). Default 80.</summary>
  public int WebpQuality { get; set; } = 80;

  /// <summary>Jakość WebP miniatury (1-100). Default 70.</summary>
  public int ThumbnailWebpQuality { get; set; } = 70;
}
