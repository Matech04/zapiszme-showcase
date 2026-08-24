using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Api.Health;

/// <summary>
/// Smoke-check sprawdzający, czy krytyczne klucze konfiguracji prod są wypełnione.
/// Działa jako warstwa "early warning" — bez Azure Comm, Turnstile lub Dashboard URL
/// aplikacja co prawda wstaje (ValidateProductionConfiguration łapie najgorsze braki),
/// ale nie wszystkie ścieżki (np. wysyłka maili, OTP do klientów) zadziałają.
///
/// Endpoint: <c>/health/smoke</c> (zob. Program.cs MapHealthEndpoints).
/// </summary>
public sealed class ConfigurationHealthCheck : IHealthCheck
{
  private readonly IConfiguration _configuration;
  private readonly IHostEnvironment _environment;

  public ConfigurationHealthCheck(IConfiguration configuration, IHostEnvironment environment)
  {
    _configuration = configuration;
    _environment = environment;
  }

  public Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default)
  {
    // W Testing / Development brak konfiguracji to OK — nie chcemy false-negative w CI.
    if (!_environment.IsProduction())
    {
      return Task.FromResult(HealthCheckResult.Healthy("Non-production: configuration smoke check pominięty."));
    }

    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(_configuration["Dashboard:BaseUrl"]))
    {
      missing.Add("Dashboard:BaseUrl");
    }

    var cors = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (cors is null || cors.Length == 0)
    {
      missing.Add("Cors:AllowedOrigins");
    }

    if (string.IsNullOrWhiteSpace(_configuration["AzureCommunication:Email:ConnectionString"]))
    {
      missing.Add("AzureCommunication:Email:ConnectionString");
    }

    if (string.IsNullOrWhiteSpace(_configuration["AzureCommunication:Email:SenderAddress"]))
    {
      missing.Add("AzureCommunication:Email:SenderAddress");
    }

    if (string.IsNullOrWhiteSpace(_configuration["AzureCommunication:Email:FeedbackRecipientEmail"]))
    {
      missing.Add("AzureCommunication:Email:FeedbackRecipientEmail");
    }

    if (_configuration.GetValue<bool>("Turnstile:Enabled")
      && string.IsNullOrWhiteSpace(_configuration["Turnstile:SecretKey"]))
    {
      missing.Add("Turnstile:SecretKey");
    }

    var trustedProxies = _configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>();
    if (trustedProxies is null || trustedProxies.Length == 0)
    {
      missing.Add("ReverseProxy:TrustedProxies");
    }

    if (missing.Count > 0)
    {
      // Degraded zamiast Unhealthy: aplikacja jeszcze działa dla części flow,
      // ale ops powinien wpiąć monitoring.
      return Task.FromResult(HealthCheckResult.Degraded(
        $"Brakuje kluczy konfiguracji: {string.Join(", ", missing)}"));
    }

    return Task.FromResult(HealthCheckResult.Healthy("Wszystkie krytyczne klucze prod są ustawione."));
  }
}
