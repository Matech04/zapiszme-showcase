using App.Api.Security;
using App.Application.Booking.BookingAppointments.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;


namespace App.Api.Controllers.Booking;

public record CreateAppointmentDto(IReadOnlyList<Guid> ServiceIds, Guid EmployeeId, DateOnly Date, TimeOnly StartTime, string? TurnstileToken = null);
public record UpdateAppointmentDto(Guid Token, IReadOnlyList<Guid> ServiceIds, Guid EmployeeId, DateOnly Date, TimeOnly StartTime, string? TurnstileToken = null);


[AllowAnonymous]
[Route("api/booking/{slug}/public-appointment")]
public class PublicCreateAppointmentController : BookingApiControllerBase
{
  // Anti-abuse warstwa 2: anonimowa sesja w cookie. Server-side trzymane, JS nie czyta (HttpOnly),
  // 30min TTL — pokrywa wiele kolejnych slotów wybieranych przez tego samego usera.
  // Każdy nowy /hold tej sesji auto-anuluje poprzednie Pending (handler).
  private const string AnonSessionCookieName = "booking_saas_anon_session";
  private static readonly TimeSpan AnonSessionCookieTtl = TimeSpan.FromMinutes(30);

  private readonly IWebHostEnvironment _environment;

  public PublicCreateAppointmentController(IWebHostEnvironment environment)
  {
    _environment = environment;
  }

  /// <summary>Slug w URL służy middleware do ustawienia tenanta — musi być ten sam co w panelu booking.</summary>
  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("hold", Name = "PublicCreateAppointmentHold")]
  public async Task<ActionResult<PublicBookingHoldDto>> PublicCreateHold(
    [FromRoute] string slug,
    [FromBody] CreateAppointmentDto request,
    [FromServices] ITurnstileVerifier turnstileVerifier,
    CancellationToken cancellationToken)
  {
    _ = slug;
    // Invisible Turnstile — anti-bot vs distributed flood (per-IP rate limit nie złapie
    // botnetu rozsianego po wielu IP, Turnstile złapie). W Testing/Dev verifier przepuszcza.
    var turnstileValid = await turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      TurnstileWidgetKind.Invisible,
      cancellationToken);
    if (!turnstileValid)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zweryfikować zabezpieczenia.",
        Detail = "Odśwież stronę i spróbuj ponownie.",
      });
    }

    var anonSessionId = ResolveOrIssueAnonSession();
    var result = await Mediator.Send(
      new CreateBookingAppointmentCommand(
        request.EmployeeId, request.ServiceIds, request.Date, request.StartTime,
        AnonSessionId: anonSessionId),
      cancellationToken);
    return Ok(result);
  }

  /// <summary>
  /// Resolves anonymous session id z cookie lub generuje nowe (HttpOnly + polityka cross-site
  /// z <see cref="CrossOriginCookiePolicy"/>).
  /// Cookie odświeża się przy każdym /hold — sesja żyje dopóki user aktywnie rezerwuje.
  /// </summary>
  private Guid ResolveOrIssueAnonSession()
  {
    if (Request.Cookies.TryGetValue(AnonSessionCookieName, out var raw)
        && Guid.TryParse(raw, out var existing))
    {
      RefreshAnonSessionCookie(existing);
      return existing;
    }

    var fresh = Guid.NewGuid();
    RefreshAnonSessionCookie(fresh);
    return fresh;
  }

  private void RefreshAnonSessionCookie(Guid sessionId)
  {
    var options = new CookieOptions
    {
      HttpOnly = true,                     // chroni przed XSS-em / odczytem z JS
      MaxAge = AnonSessionCookieTtl,
      IsEssential = true,                  // funkcjonalne — nie wymaga consent
    };
    // booking-web i api to różne *site* (api.localhost ≠ localhost, api.zapisz.me ≠ zapisz.me),
    // a to cookie leci w cross-site fetch z `credentials: 'include'` — w prod MUSI mieć
    // SameSite=None+Secure, inaczej przeglądarka go nie wyśle i anti-squatting
    // (CancelPreviousHoldsForAnonSession) nie ma czego dopasować.
    CrossOriginCookiePolicy.Apply(options, _environment, Request.IsHttps);
    Response.Cookies.Append(AnonSessionCookieName, sessionId.ToString("D"), options);
  }

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPatch("{id}")]
  public async Task<ActionResult<HoldLease>> PublicUpdateAppointment(
    [FromRoute] string slug,
    [FromBody] UpdateAppointmentDto request,
    Guid id,
    [FromServices] ITurnstileVerifier turnstileVerifier,
    CancellationToken cancellationToken)
  {
    _ = slug;
    var turnstileValid = await turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      TurnstileWidgetKind.Invisible,
      cancellationToken);
    if (!turnstileValid)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zweryfikować zabezpieczenia.",
        Detail = "Odśwież stronę i spróbuj ponownie.",
      });
    }

    var result = await Mediator.Send(
      new UpdatePublicAppointmentCommand(id, request.Token, request.EmployeeId, request.ServiceIds, request.Date, request.StartTime),
      cancellationToken);
    return Ok(result);
  }
}


