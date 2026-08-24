using App.Application.Booking.BookingServices.Dtos;
using App.Application.Booking.BookingServices.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

[AllowAnonymous]
[EnableRateLimiting("PublicBookingRead")]
[Route("api/booking/{slug}/services")]
public class BookingServicesController : BookingApiControllerBase
{
  [HttpGet(Name = "GetBookingServices")]
  public async Task<ActionResult<List<BookingServiceDto>>> GetServices(
      [FromQuery] Guid? categoryId,
      [FromQuery] Guid? employeeId)
  {
    var result = await Mediator.Send(new GetBookingServicesQuery(categoryId, employeeId));
    return Ok(result);
  }

  [HttpGet("{id:guid}", Name = "GetBookingServiceById")]
  public async Task<ActionResult<BookingServiceDto>> GetService(Guid id)
  {
    var result = await Mediator.Send(new GetBookingServiceByIdQuery(id));
    return Ok(result);
  }
}
