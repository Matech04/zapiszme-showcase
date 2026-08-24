using App.Application.ShiftTemplates.Commands.CreateShiftTemplate;
using App.Application.ShiftTemplates.Commands.DeleteShiftTemplate;
using App.Application.ShiftTemplates.Commands.UpdateShiftTemplate;
using App.Application.ShiftTemplates.Dtos;
using App.Application.ShiftTemplates.Queries.GetShiftTemplateById;
using App.Application.ShiftTemplates.Queries.GetShiftTemplates;
using App.Domain.Aggregates.EmployeeAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Szablony zmian są obiektem WSPÓŁDZIELONYM przez cały salon (`ShiftTemplate` ma `TenantId`,
/// nie ma `EmployeeId`), a usuwanie jest twarde (brak `ISoftDelete`). Dlatego cały kontroler —
/// łącznie z odczytem — jest zamknięty na `StaffManagement` (Owner/Manager/Admin). Employee i
/// Kiosk nie widzą szablonów; front nie pobiera ich w trybie „moja dostępność".
/// </summary>
[Authorize(Policy = "StaffManagement")]
public class ShiftTemplatesController : ApiControllerBase
{
  [HttpGet(Name = "GetShiftTemplates")]
  public async Task<ActionResult<List<ShiftTemplateDto>>> GetShiftTemplates()
  {
    var result = await Mediator.Send(new GetShiftTemplatesQuery());
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetShiftTemplateById")]
  public async Task<ActionResult<ShiftTemplateDto>> GetShiftTemplateById(Guid id)
  {
    var result = await Mediator.Send(new GetShiftTemplateByIdQuery(id));
    return Ok(result);
  }

  [HttpPost]
  public async Task<ActionResult<Guid>> CreateShiftTemplate(CreateShiftTemplateCommand command)
  {
    var id = await Mediator.Send(command);
    return Ok(id);
  }

  [HttpPut("{id}", Name = "UpdateShiftTemplate")]
  public async Task<ActionResult> UpdateShiftTemplate(Guid id, UpdateShiftTemplateRequest request)
  {
    await Mediator.Send(new UpdateShiftTemplateCommand(
      id, request.Name, request.SlotGenerationMode, request.WorkRanges, request.Breaks, request.FixedStartTimes));
    return NoContent();
  }

  [HttpDelete("{id}")]
  public async Task<ActionResult> DeleteShiftTemplate(Guid id)
  {
    await Mediator.Send(new DeleteShiftTemplateCommand(id));
    return NoContent();
  }
}

public record UpdateShiftTemplateRequest(
  string Name,
  SlotGenerationMode SlotGenerationMode,
  IReadOnlyCollection<TimeRangeDto> WorkRanges,
  IReadOnlyCollection<TimeRangeDto> Breaks,
  IReadOnlyCollection<TimeOnly>? FixedStartTimes
);
