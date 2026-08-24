using App.Application.Notifications.SmsTemplates.Commands.SubmitSmsTemplate;
using App.Application.Notifications.SmsTemplates.Dtos;
using App.Application.Notifications.SmsTemplates.Queries.GetSmsTemplates;
using App.Application.Notifications.SmsTemplates.Queries.PreviewSmsTemplate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Personalizacja treści SMS do klienta przez salon. Edycja trafia do kolejki „oczekuje na akceptację"
/// (moderacja przez administratora platformy). Tylko właściciel (BusinessManagement).
/// </summary>
[Route("api/sms-templates")]
[Authorize(Policy = "BusinessManagement")]
public class SmsTemplatesController : ApiControllerBase
{
  /// <summary>Lista personalizowalnych typów SMS salonu (status, treść, placeholdery, podgląd domyślnej).</summary>
  [HttpGet]
  public async Task<ActionResult<List<SmsTemplateDto>>> Get()
  {
    var result = await Mediator.Send(new GetSmsTemplatesQuery());
    return Ok(result);
  }

  /// <summary>Zapisuje treść szablonu danego typu → kolejka akceptacji.</summary>
  [HttpPut("{notificationType}")]
  public async Task<ActionResult> Submit(NotificationType notificationType, [FromBody] SubmitSmsTemplateBody body)
  {
    await Mediator.Send(new SubmitSmsTemplateCommand(notificationType, body.Body));
    return NoContent();
  }

  /// <summary>
  /// Podgląd finalnej treści SMS — ta sama transliteracja ASCII + clip + przykładowe dane co przy
  /// wysyłce. Owner widzi „co naprawdę wyjdzie" zanim zapisze (placeholdery {…} → wartości, „ż" → „z").
  /// </summary>
  [HttpPost("{notificationType}/preview")]
  public async Task<ActionResult<SmsTemplatePreviewResult>> Preview(
    NotificationType notificationType,
    [FromBody] SubmitSmsTemplateBody body)
  {
    var result = await Mediator.Send(new PreviewSmsTemplateQuery(notificationType, body.Body));
    return Ok(result);
  }
}

public sealed record SubmitSmsTemplateBody(string Body);
