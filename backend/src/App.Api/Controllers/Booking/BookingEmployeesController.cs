using App.Application.Booking.BookingEmployees.Dtos;
using App.Application.Booking.BookingEmployees.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

[AllowAnonymous]
[EnableRateLimiting("PublicBookingRead")]
[Route("api/booking/{slug}/employees")]
public class BookingEmployeesController : BookingApiControllerBase
{
  [HttpGet]
  public async Task<ActionResult<List<BookingEmployeeDto>>> GetEmployees([FromQuery] Guid[] serviceIds)
  {
    var result = await Mediator.Send(new GetBookingEmployeesQuery(serviceIds));
    return Ok(result);
  }
}
