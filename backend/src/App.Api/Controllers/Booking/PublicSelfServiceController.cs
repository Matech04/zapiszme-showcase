using App.Api.Security;
using App.Application.Booking.SelfService.Commands;
using App.Application.Booking.SelfService.Dtos;
using App.Application.Booking.SelfService.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

public record SelfServiceContactRequest(string? Email, string? PhoneNumber, string? TurnstileToken = null);

public record SelfServiceVerifyRequest(string? Email, string? PhoneNumber, string Otp);

public record SelfServiceVerifyResponse(Guid SessionToken, DateTime ExpiresAtUtc);

public record SelfServiceRescheduleRequest(Guid EmployeeId, Guid ServiceId, DateOnly Date, TimeOnly StartTime);

[AllowAnonymous]
[Route("api/booking/{slug}/self-service")]
public class PublicSelfServiceController : BookingApiControllerBase
{
  private const string SessionHeader = "X-SelfService-Token";

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("request-otp")]
  public async Task<ActionResult> RequestOtp(
      [FromRoute] string slug,
      [FromBody] SelfServiceContactRequest request,
      [FromServices] ITurnstileVerifier turnstileVerifier,
      CancellationToken ct)
  {
    // Invisible Turnstile — pierwsza linia obrony przed scripted OTP-abuse.
    var turnstileValid = await turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      TurnstileWidgetKind.Invisible,
      ct);
    if (!turnstileValid)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zweryfikować zabezpieczenia.",
        Detail = "Odśwież stronę i spróbuj ponownie.",
      });
    }

    await Mediator.Send(
      new RequestSelfServiceOtpCommand(slug, request.Email, request.PhoneNumber),
      ct);
    return Ok();
  }

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("verify-otp")]
  public async Task<ActionResult<SelfServiceVerifyResponse>> VerifyOtp(
      [FromRoute] string slug,
      [FromBody] SelfServiceVerifyRequest request,
      CancellationToken ct)
  {
    var result = await Mediator.Send(
      new VerifySelfServiceOtpCommand(slug, request.Email, request.PhoneNumber, request.Otp),
      ct);
    return Ok(new SelfServiceVerifyResponse(result.SessionToken, result.ExpiresAtUtc));
  }

  [HttpGet("appointments")]
  public async Task<ActionResult<List<SelfServiceAppointmentDto>>> List(
      [FromRoute] string slug,
      CancellationToken ct)
  {
    if (!TryGetSession(out var token))
    {
      return Unauthorized();
    }

    var list = await Mediator.Send(new GetSelfServiceAppointmentsQuery(slug, token), ct);
    return Ok(list);
  }

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("appointments/{id:guid}/cancel")]
  public async Task<ActionResult> Cancel(
      [FromRoute] string slug,
      [FromRoute] Guid id,
      CancellationToken ct)
  {
    if (!TryGetSession(out var token))
    {
      return Unauthorized();
    }

    await Mediator.Send(new CancelSelfServiceAppointmentCommand(slug, token, id), ct);
    return Ok();
  }

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("appointments/{id:guid}/reschedule")]
  public async Task<ActionResult> Reschedule(
      [FromRoute] string slug,
      [FromRoute] Guid id,
      [FromBody] SelfServiceRescheduleRequest request,
      CancellationToken ct)
  {
    if (!TryGetSession(out var token))
    {
      return Unauthorized();
    }

    await Mediator.Send(
      new RescheduleSelfServiceAppointmentCommand(
        slug, token, id, request.EmployeeId, request.ServiceId, request.Date, request.StartTime),
      ct);
    return Ok();
  }

  /// <summary>Self-service token przekazywany jest w nagłówku <c>X-SelfService-Token</c>
  /// (zamiast standardowego <c>Authorization: Bearer</c>, by nie kolidować ze schematami JWT/Identity).</summary>
  private bool TryGetSession(out Guid token)
  {
    if (Request.Headers.TryGetValue(SessionHeader, out var raw)
      && Guid.TryParse(raw.ToString(), out token))
    {
      return true;
    }

    token = Guid.Empty;
    return false;
  }
}
