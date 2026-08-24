using App.Application.Appointments.Dtos;
using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Booking.BookingAppointments.Dtos;
using App.Application.Booking.BookingAppointments.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers.Booking;

[AllowAnonymous]
[Route("api/booking/{slug}/appointments")]
public class BookingAppointmentsController : BookingApiControllerBase
{
  [EnableRateLimiting("PublicBookingRead")]
  [HttpGet("available-slots", Name = "GetBookingAvailableSlots")]
  public async Task<ActionResult<List<AppointmentSlotDto>>> GetAvailableSlots(
      [FromQuery] DateOnly date,
      [FromQuery] Guid employeeId,
      [FromQuery] Guid[] serviceIds)
  {
    var result = await Mediator.Send(new GetBookingAvailableTimeSlotsQuery(date, employeeId, serviceIds));
    return Ok(result);
  }

  [EnableRateLimiting("PublicBookingRead")]
  [HttpGet("month-availability", Name = "GetBookingMonthAvailability")]
  public async Task<ActionResult<BookingMonthAvailabilityDto>> GetMonthAvailability(
      [FromQuery] int year,
      [FromQuery] int month,
      [FromQuery] Guid employeeId,
      [FromQuery] Guid[] serviceIds)
  {
    var result = await Mediator.Send(new GetBookingMonthAvailabilityQuery(year, month, employeeId, serviceIds));
    return Ok(result);
  }

  // USUNIĘTY duplikat POST /appointments (hold). Tworzenie holdu idzie WYŁĄCZNIE przez
  // PublicCreateAppointmentController /public-appointment/hold — tam jest Turnstile (anti-bot) i
  // serwerowy AnonSessionId w HttpOnly cookie. Stary endpoint przyjmował AnonSessionId z body
  // i nie miał Turnstile, więc omijał anti-squatting (rotacja/pominięcie AnonSessionId = brak
  // auto-anulowania poprzednich holdów) — wektor DoS dostępności kalendarza.
}
