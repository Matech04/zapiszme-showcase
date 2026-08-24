using Amazon.S3;
using Amazon.S3.Model;
using App.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Storage;

/// <summary>
/// Implementacja <see cref="IFileStorage"/> na AWSSDK.S3. Mówi protokołem S3, więc obsługuje
/// zarówno Cloudflare R2 (prod), jak i MinIO (dev) — różni je tylko endpoint + ForcePathStyle.
/// Klient S3 (<see cref="IAmazonS3"/>) jest rejestrowany jako singleton w DI.
/// </summary>
public sealed class S3FileStorage : IFileStorage
{
  private readonly IAmazonS3 _s3;
  private readonly StorageOptions _options;
  private readonly ILogger<S3FileStorage> _logger;

  public S3FileStorage(
    IAmazonS3 s3,
    IOptions<StorageOptions> options,
    ILogger<S3FileStorage> logger)
  {
    _s3 = s3;
    _options = options.Value;
    _logger = logger;
  }

  public async Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default)
  {
    // Timeout per-operacja: zablokowany/wolny storage nie może w nieskończoność trzymać
    // wątku requestu (i puli połączeń). Linkujemy z tokenem żądania.
    using var timeoutCts = CreateTimeoutScope(ct, out var linkedToken);

    var request = new PutObjectRequest
    {
      BucketName = _options.Bucket,
      Key = key,
      InputStream = content,
      ContentType = contentType,
      AutoCloseStream = false,
      // Cloudflare R2 NIE obsługuje streaming payload signing (STREAMING-AWS4-HMAC-SHA256-PAYLOAD),
      // którego AWS SDK v4 używa domyślnie → "not implemented" (500 na każdym uploadzie). Wymuszamy
      // UNSIGNED-PAYLOAD — ale AWS SDK dopuszcza to WYŁĄCZNIE po HTTPS. MinIO w dev jedzie po HTTP,
      // więc tam zostawiamy domyślny podpis (obsługuje go bez problemu). Patrz StorageOptions.
      DisablePayloadSigning = _options.DisablePayloadSigning,
    };

    try
    {
      await _s3.PutObjectAsync(request, linkedToken);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
    {
      _logger.LogError("Storage upload timed out after {Seconds}s (key {Key})", _options.OperationTimeoutSeconds, key);
      throw new TimeoutException($"Storage upload timed out for key '{key}'.");
    }

    return BuildPublicUrl(key);
  }

  public async Task DeleteAsync(string key, CancellationToken ct = default)
  {
    using var timeoutCts = CreateTimeoutScope(ct, out var linkedToken);

    try
    {
      await _s3.DeleteObjectAsync(_options.Bucket, key, linkedToken);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
    {
      _logger.LogError("Storage delete timed out after {Seconds}s (key {Key})", _options.OperationTimeoutSeconds, key);
      throw new TimeoutException($"Storage delete timed out for key '{key}'.");
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      // Idempotentne: usuwanie nieistniejącego obiektu nie jest błędem.
    }
  }

  public string BuildPublicUrl(string key)
  {
    var baseUrl = _options.PublicBaseUrl.TrimEnd('/');
    return $"{baseUrl}/{key}";
  }

  private CancellationTokenSource CreateTimeoutScope(CancellationToken ct, out CancellationToken linkedToken)
  {
    var seconds = _options.OperationTimeoutSeconds > 0 ? _options.OperationTimeoutSeconds : 15;
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(seconds));
    linkedToken = cts.Token;
    return cts;
  }
}
