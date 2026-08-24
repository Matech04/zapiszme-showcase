using App.Application.Admin.SmsTemplates.Commands.ApproveSmsTemplate;
using App.Application.Admin.SmsTemplates.Commands.RejectSmsTemplate;
using App.Application.Admin.SmsTemplates.Dtos;
using App.Application.Admin.SmsTemplates.Queries.GetPendingSmsTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Moderacja custom szablonów SMS przez administratora platformy (cross-tenant). Tylko SystemAdmin.
/// </summary>
[Authorize(Policy = "SystemAdminOnly")]
[Route("api/admin/sms-templates")]
public class AdminSmsTemplatesController : ApiControllerBase
{
  /// <summary>Kolejka szablonów oczekujących na akceptację (wszystkie salony).</summary>
  [HttpGet("pending")]
  public async Task<ActionResult<List<PendingSmsTemplateDto>>> Pending()
  {
    var result = await Mediator.Send(new GetPendingSmsTemplatesQuery());
    return Ok(result);
  }

  /// <summary>Zatwierdza oczekujący szablon — staje się aktywny przy wysyłce.</summary>
  [HttpPost("{templateId:guid}/approve")]
  public async Task<ActionResult> Approve(Guid templateId)
  {
    await Mediator.Send(new ApproveSmsTemplateCommand(templateId));
    return NoContent();
  }

  /// <summary>Odrzuca oczekujący szablon (z powodem). Poprzednia zatwierdzona treść pozostaje aktywna.</summary>
  [HttpPost("{templateId:guid}/reject")]
  public async Task<ActionResult> Reject(Guid templateId, [FromBody] RejectSmsTemplateBody body)
  {
    await Mediator.Send(new RejectSmsTemplateCommand(templateId, body.Reason));
    return NoContent();
  }
}

public sealed record RejectSmsTemplateBody(string? Reason);
