using App.Application.Admin.PlatformMaintenance.Commands;
using App.Application.Admin.PlatformMaintenance.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Globalny tryb serwisowy platformy (kill-switch). Tylko admin platformy (SystemAdminOnly).
/// Gdy włączony — <c>PlatformMaintenanceMiddleware</c> blokuje wszystkie operacje zapisu na
/// WSZYSTKICH kontach poza adminem platformy (ochrona danych na czas buga/ataku/migracji).
/// </summary>
[Authorize(Policy = "SystemAdminOnly")]
[Route("api/admin/system/maintenance")]
public class AdminMaintenanceController : ApiControllerBase
{
  /// <summary>Bieżący stan globalnego trybu serwisowego.</summary>
  [HttpGet]
  public async Task<ActionResult<MaintenanceStatusDto>> Get(CancellationToken ct)
  {
    var result = await Mediator.Send(new GetMaintenanceStatusQuery(), ct);
    return Ok(result);
  }

  /// <summary>Włącza/wyłącza globalny tryb serwisowy platformy (instant toggle + opcjonalny komunikat).</summary>
  [HttpPut]
  public async Task<ActionResult> Set([FromBody] SetMaintenanceModeRequest request, CancellationToken ct)
  {
    await Mediator.Send(new SetMaintenanceModeCommand(request.Enabled, request.Message), ct);
    return NoContent();
  }
}
