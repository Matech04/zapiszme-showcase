using App.Application.Booking.BookingServiceCategories.Queries;
using App.Application.ServiceCategories.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

[AllowAnonymous]
[EnableRateLimiting("PublicBookingRead")]
[Route("api/booking/{slug}/service-categories")]
public class BookingServiceCategoriesController : BookingApiControllerBase
{
  [HttpGet]
  public async Task<ActionResult<List<ServiceCategoryDto>>> GetServiceCategories()
  {
    var result = await Mediator.Send(new GetBookingServiceCategoriesQuery());
    return Ok(result);
  }
}
