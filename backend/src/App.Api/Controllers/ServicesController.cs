using App.Application.Services.Commands.CreateService;
using App.Application.Services.Commands.DeleteService;
using App.Application.Services.Commands.ReorderServices;
using App.Application.Services.Commands.SetServiceEmployees;
using App.Application.Services.Commands.UpdateService;
using App.Application.Services.Dtos;
using App.Application.Services.Queries.GetServiceById;
using App.Application.Services.Queries.GetServiceEmployees;
using App.Application.Services.Queries.GetServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")]
public class ServicesController : ApiControllerBase
{
  [HttpGet(Name = "GetServices")]
  public async Task<ActionResult<List<ServiceDto>>> GetServices([FromQuery] Guid? categoryId)
  {
    var result = await Mediator.Send(new GetServicesQuery(categoryId));
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetServiceById")]
  public async Task<ActionResult<ServiceDto>> GetService(Guid id)
  {
    var result = await Mediator.Send(new GetServiceByIdQuery(id));
    return Ok(result);
  }
  [Authorize(Policy = "StaffManagement")]
  [HttpPost]
  public async Task<ActionResult<Guid>> CreateService(CreateServiceCommand command)
  {
    var serviceId = await Mediator.Send(command);
    return Ok(serviceId);
  }
  [Authorize(Policy = "StaffManagement")]
  [HttpPut("{id}")]
  public async Task<ActionResult> UpdateService(Guid id, UpdateServiceCommand command)
  {
    if (id != command.Id)
    {
      return BadRequest();
    }

    await Mediator.Send(command);
    return NoContent();
  }
  [Authorize(Policy = "StaffManagement")]
  [HttpDelete("{id}")]
  public async Task<ActionResult> DeleteService(Guid id)
  {
    await Mediator.Send(new DeleteServiceCommand(id));
    return NoContent();
  }

  [Authorize(Policy = "StaffManagement")]
  [HttpPut("reorder")]
  public async Task<ActionResult> ReorderServices(ReorderServicesCommand command)
  {
    await Mediator.Send(command);
    return NoContent();
  }

  [HttpGet("{id}/employees", Name = "GetServiceEmployees")]
  public async Task<ActionResult<List<Guid>>> GetServiceEmployees(Guid id)
  {
    var result = await Mediator.Send(new GetServiceEmployeesQuery(id));
    return Ok(result);
  }

  [Authorize(Policy = "StaffManagement")]
  [HttpPut("{id}/employees")]
  public async Task<ActionResult> SetServiceEmployees(Guid id, SetServiceEmployeesCommand command)
  {
    if (id != command.ServiceId)
    {
      return BadRequest();
    }

    await Mediator.Send(command);
    return NoContent();
  }
}