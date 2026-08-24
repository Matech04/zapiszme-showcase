using App.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Cyklicznie odświeża <see cref="ICustomDomainRegistry"/> ze stanu bazy (<c>Tenant.CustomDomain</c>).
/// Pierwszy refresh tuż po starcie, potem co <c>WhiteLabel:RegistryRefreshMinutes</c> (default 5).
/// W Testing nie startuje (zob. Program.cs) — snapshot pozostaje pusty, co jest OK (white-label nie
/// jest używane w testach jednostkowych/integracyjnych).
/// </summary>
public sealed class CustomDomainRegistryRefresher : BackgroundService
{
  private readonly ICustomDomainRegistry _registry;
  private readonly ILogger<CustomDomainRegistryRefresher> _logger;
  private readonly TimeSpan _interval;

  public CustomDomainRegistryRefresher(
    ICustomDomainRegistry registry,
    IConfiguration configuration,
    ILogger<CustomDomainRegistryRefresher> logger)
  {
    _registry = registry;
    _logger = logger;
    var minutes = configuration.GetValue<double?>("WhiteLabel:RegistryRefreshMinutes") ?? 5d;
    _interval = TimeSpan.FromMinutes(minutes);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await _registry.RefreshAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Odświeżanie CustomDomainRegistry — błąd cyklu.");
      }

      try
      {
        await Task.Delay(_interval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }
}
