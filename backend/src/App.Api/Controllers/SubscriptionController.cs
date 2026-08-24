using App.Application.Subscription.Commands.ChangeSeats;
using App.Application.Subscription.Dtos;
using App.Application.Subscription.Queries.GetSubscriptionInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

// Billing/subskrypcja to operacja właścicielska (zmiana liczby płatnych miejsc = realne pieniądze).
// BusinessManagement = Owner/Admin; Manager NIE może zmieniać ani podglądać rozliczeń salonu.
[Authorize(Policy = "BusinessManagement")]
public class SubscriptionController : ApiControllerBase
{
  [HttpGet]
  public async Task<ActionResult<SubscriptionInfoDto>> Get()
  {
    var result = await Mediator.Send(new GetSubscriptionInfoQuery());
    return Ok(result);
  }

  [HttpPost("seats")]
  public async Task<ActionResult<ChangeSeatsResult>> ChangeSeats([FromBody] ChangeSeatsRequest request)
  {
    var result = await Mediator.Send(new ChangeSeatsCommand(request.NewSeats));
    return Ok(result);
  }
}

public sealed record ChangeSeatsRequest(int NewSeats);
