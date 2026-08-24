using App.Application.Booking.BookingSalon.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

[AllowAnonymous]
[EnableRateLimiting("PublicBookingRead")]
[Route("api/booking/{slug}/public-salon")]
public class PublicBookingSalonController : BookingApiControllerBase
{
  [HttpGet]
  public async Task<ActionResult<PublicBookingSalonInfoDto>> Get([FromRoute] string slug, CancellationToken cancellationToken)
  {
    var result = await Mediator.Send(new GetPublicBookingSalonQuery(slug), cancellationToken);
    return Ok(result);
  }
}
