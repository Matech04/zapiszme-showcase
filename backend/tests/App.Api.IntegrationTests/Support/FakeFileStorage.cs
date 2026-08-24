using App.Application.Common.Interfaces;

namespace App.Api.E2eSupport;

/// <summary>
/// Testowy <see cref="IFileStorage"/> — bez realnego S3/R2/MinIO. Zwraca deterministyczny URL i nie
/// wykonuje żadnego I/O. Realna implementacja konstruuje AmazonS3Client, który bez skonfigurowanego
/// ServiceURL/region rzuca przy starcie żądania — co wywracało ścieżki dotykające storage (np.
/// UpdateService, który best-effort kasuje osierocone obrazy) w środowisku testowym.
/// </summary>
public sealed class FakeFileStorage : IFileStorage
{
  public Task<string> UploadAsync(Stream content, string key, string contentType, CancellationToken ct = default)
    => Task.FromResult(BuildPublicUrl(key));

  public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

  public string BuildPublicUrl(string key) => $"https://test-storage.local/{key}";
}
