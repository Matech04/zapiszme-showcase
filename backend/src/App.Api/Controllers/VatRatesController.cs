using App.Application.VatRates.Commands.CreateVatRate;
using App.Application.VatRates.Commands.DeleteVatRate;
using App.Application.VatRates.Commands.UpdateVatRate;
using App.Application.VatRates.Dtos;
using App.Application.VatRates.Queries.GetVatRates;
using App.Application.VatRates.Queries.GetVatRateById;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")]
public class VatRatesController : ApiControllerBase
{
  [HttpGet(Name = "GetVatRates")]
  public async Task<ActionResult<List<VatRateDto>>> GetVatRates()
  {
    var result = await Mediator.Send(new GetVatRatesQuery());
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetVatRateById")]
  public async Task<ActionResult<VatRateDto>> GetVatRate(Guid id)
  {
    var result = await Mediator.Send(new GetVatRateByIdQuery(id));
    return Ok(result);
  }

  [HttpPost]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult<Guid>> CreateVatRate(CreateVatRateCommand command)
  {
    var vatRateId = await Mediator.Send(command);
    return Ok(vatRateId);
  }

  [HttpPut("{id}")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult> UpdateVatRate(Guid id, UpdateVatRateCommand command)
  {
    if (id != command.Id)
    {
      return BadRequest();
    }

    await Mediator.Send(command);
    return NoContent();
  }

  [HttpDelete("{id}")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult> DeleteVatRate(Guid id)
  {
    await Mediator.Send(new DeleteVatRateCommand(id));
    return NoContent();
  }
}