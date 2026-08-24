using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Endpointy wewnętrzne (sieć docker) — NIE wystawiać publicznie. Caddy blokuje <c>/internal/*</c>
/// na edge (zob. caddyfile). Wykluczone z OpenAPI/NSwag.
/// </summary>
[AllowAnonymous]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal")]
public class InternalController : ControllerBase
{
  private readonly ICustomDomainRegistry _registry;

  public InternalController(ICustomDomainRegistry registry) => _registry = registry;

  /// <summary>
  /// Ask-endpoint dla Caddy On-Demand TLS: <c>200</c> gdy host należy do zarejestrowanej white-label
  /// domeny, inaczej <c>404</c>. Tani odczyt ze snapshotu w pamięci — bezpieczny przy floodzie nieznanych
  /// SNI (zero I/O). Wyłączony spod per-IP rate limitu (Caddy odpytuje z jednego IP) — zob. Program.cs.
  /// </summary>
  [HttpGet("tls-allowed")]
  public IActionResult TlsAllowed([FromQuery] string domain)
      => _registry.IsManagedHost(domain) ? Ok() : NotFound();
}
