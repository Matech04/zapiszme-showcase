namespace App.Infrastructure.Storage;

/// <summary>
/// Konfiguracja warstwy storage (sekcja <c>Storage</c> w appsettings).
///
/// PROD = Cloudflare R2 (S3-compatible), LOKALNIE = MinIO (S3-compatible) — ten sam kod, inny
/// endpoint/provider. SEKRETY (<see cref="AccessKey"/>/<see cref="SecretKey"/>) TYLKO przez zmienne
/// środowiskowe (<c>STORAGE__ACCESSKEY</c>, <c>STORAGE__SECRETKEY</c>) — NIGDY w pliku appsettings.
/// </summary>
public sealed class StorageOptions
{
  public const string SectionName = "Storage";

  /// <summary>Provider: <c>R2</c> (prod, Cloudflare R2) lub <c>MinIO</c> (dev). Steruje ForcePathStyle.</summary>
  public string Provider { get; set; } = "MinIO";

  /// <summary>
  /// Endpoint S3 API. R2: <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>.
  /// MinIO dev: <c>http://localhost:9000</c>.
  /// </summary>
  public string Endpoint { get; set; } = string.Empty;

  /// <summary>Nazwa bucketu, do którego trafiają obiekty.</summary>
  public string Bucket { get; set; } = string.Empty;

  /// <summary>
  /// Bazowy publiczny URL odczytu obiektów (bez końcowego slasha). R2: publiczny URL bucketu
  /// (r2.dev albo custom domain podpięta w Cloudflare). MinIO dev: <c>http://localhost:9000/&lt;bucket&gt;</c>.
  /// Publiczny URL składamy jako <c>{PublicBaseUrl}/{key}</c>.
  /// </summary>
  public string PublicBaseUrl { get; set; } = string.Empty;

  /// <summary>Region S3. R2 ignoruje region, ale SDK wymaga niepustej wartości — default <c>auto</c>.</summary>
  public string Region { get; set; } = "auto";

  /// <summary>Access key id. SEKRET — z env <c>STORAGE__ACCESSKEY</c>, nie z pliku.</summary>
  public string AccessKey { get; set; } = string.Empty;

  /// <summary>Secret access key. SEKRET — z env <c>STORAGE__SECRETKEY</c>, nie z pliku.</summary>
  public string SecretKey { get; set; } = string.Empty;

  /// <summary>Timeout pojedynczej operacji S3 (upload/delete) w sekundach. Default 15s.</summary>
  public int OperationTimeoutSeconds { get; set; } = 15;

  /// <summary>True dla MinIO (ścieżkowy styl URL-i). Dla R2/AWS path-style też działa — bezpieczny default.</summary>
  public bool ForcePathStyle => !string.Equals(Provider, "R2", StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Czy wymusić UNSIGNED-PAYLOAD (<c>DisablePayloadSigning</c>) na uploadzie. R2 nie obsługuje
  /// streaming payload signing SDK v4 (500 na każdym uploadzie) → trzeba wyłączyć. ALE AWS SDK
  /// pozwala na to WYŁĄCZNIE po HTTPS (integralność zapewnia TLS). MinIO w dev jedzie po
  /// <c>http://localhost:9000</c> → włączenie flagi rzuca "must be sent over HTTPS". Dlatego
  /// wyłączamy podpis tylko dla endpointów HTTPS; po HTTP zostawiamy domyślny (MinIO go obsługuje).
  /// </summary>
  public bool DisablePayloadSigning =>
    Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
