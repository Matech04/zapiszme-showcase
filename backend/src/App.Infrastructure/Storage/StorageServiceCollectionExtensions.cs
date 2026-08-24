using Amazon.S3;
using App.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Storage;

/// <summary>
/// Rejestracja warstwy storage (S3-compatible: R2 prod / MinIO dev) + pipeline'u obróbki obrazu.
/// Wołane z Program.cs.
/// </summary>
public static class StorageServiceCollectionExtensions
{
  public static IServiceCollection AddImageStorage(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services
      .AddOptions<StorageOptions>()
      .Bind(configuration.GetSection(StorageOptions.SectionName))
      .ValidateOnStart();

    services
      .AddOptions<ImageProcessingOptions>()
      .Bind(configuration.GetSection(ImageProcessingOptions.SectionName));

    // Klient S3 jako singleton — skonfigurowany pod R2/MinIO (ServiceURL + ForcePathStyle).
    services.AddSingleton<IAmazonS3>(sp =>
    {
      var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;

      var config = new AmazonS3Config
      {
        ServiceURL = opts.Endpoint,
        // MinIO wymaga path-style (http://host:9000/bucket/key); R2 też je akceptuje.
        ForcePathStyle = opts.ForcePathStyle,
        // R2 ignoruje region, ale SDK wymaga niepustej AuthenticationRegion przy custom endpoincie.
        AuthenticationRegion = string.IsNullOrWhiteSpace(opts.Region) ? "auto" : opts.Region,
        // AWS SDK v4 domyślnie dodaje flexible-checksums przez STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER,
        // którego Cloudflare R2 NIE obsługuje („not implemented" → 500 na każdym uploadzie). Liczymy/walidujemy
        // checksum tylko gdy faktycznie wymagany — to naprawia R2 i jest neutralne dla MinIO.
        RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
      };

      return new AmazonS3Client(opts.AccessKey, opts.SecretKey, config);
    });

    services.AddSingleton<IFileStorage, S3FileStorage>();
    services.AddSingleton<IImageProcessingService, ImageProcessingService>();

    return services;
  }
}
