using App.Api.Security;
using App.Application.Booking.BookingAppointments.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers.Booking;

public record RequestOtpRequest(
  Guid Token,
  string? PhoneNumber,
  string? Email,
  string? TurnstileToken = null,
  string? FirstName = null,
  string? LastName = null,
  bool IsReturningCustomer = false);

public record VerifyOtpRequest(
  Guid Token,
  string Otp,
  string? FirstName = null,
  string? LastName = null,
  string? InstagramNick = null);

public record ConfirmWithSessionRequest(
  Guid Token,
  Guid SessionToken,
  string? TurnstileToken = null,
  string? FirstName = null,
  string? LastName = null,
  string? InstagramNick = null);

[AllowAnonymous]
[Route("api/booking/{slug}/public-appointment")]
public class PublicOtpController : BookingApiControllerBase
{
  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("{id:guid}/request-otp")]
  public async Task<ActionResult> RequestOtp(
      [FromRoute] string slug,
      [FromRoute] Guid id,
      [FromBody] RequestOtpRequest request,
      [FromServices] ITurnstileVerifier turnstileVerifier,
      CancellationToken cancellationToken)
  {
    _ = slug;
    // Invisible Turnstile — bot-check w tle. Bez tokenu → 400 (klient musi załadować widget).
    // W Testing (Turnstile:Enabled=false) verifier zawsze przepuszcza.
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

    await Mediator.Send(
        new RequestOtpCommand(
          request.Token, id, request.PhoneNumber, request.Email,
          request.FirstName, request.LastName, request.IsReturningCustomer),
        cancellationToken);
    return Ok();
  }

  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("{id:guid}/verify-otp")]
  public async Task<ActionResult<VerifyOtpResult>> VerifyOtp(
      [FromRoute] string slug,
      [FromRoute] Guid id,
      [FromBody] VerifyOtpRequest request,
      CancellationToken cancellationToken)
  {
    _ = slug;
    var result = await Mediator.Send(
        new VerifyOtpCommand(request.Token, id, request.Otp, request.FirstName, request.LastName, request.InstagramNick),
        cancellationToken);
    return Ok(result);
  }

  // Potwierdzenie rezerwacji ważną sesją zweryfikowanego kontaktu — pomija krok OTP (brak SMS z KODEM).
  // Uwaga: samo potwierdzenie nadal wysyła płatny SMS/e-mail potwierdzający (BookingConfirmedEvent),
  // dlatego objęte jest capem per-sesja/IP (AssertCanConfirmWithSession). Zgodność kontaktu sesji z
  // holdem egzekwowana jest serwerowo (nie tylko na froncie).
  [EnableRateLimiting("PublicBookingWrite")]
  [HttpPost("{id:guid}/confirm-with-session")]
  public async Task<ActionResult<VerifyOtpResult>> ConfirmWithSession(
      [FromRoute] string slug,
      [FromRoute] Guid id,
      [FromBody] ConfirmWithSessionRequest request,
      [FromServices] ITurnstileVerifier turnstileVerifier,
      CancellationToken cancellationToken)
  {
    // Invisible Turnstile — spójnie z /hold i /request-otp. Bez tego był to JEDYNY anonimowy endpoint
    // wyzwalający płatny SMS bez bot-checku: capy per-IP (AssertCanConfirmWithSession) ograniczają
    // pojedynczy adres, ale botnet rozsiany po adresach mnożył je przez liczbę IP i dobijał globalny
    // kill-switch dobowy, ubijając OTP całej platformie.
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
        new ConfirmBookingWithSessionCommand(
          slug, id, request.Token, request.SessionToken,
          request.FirstName, request.LastName, request.InstagramNick),
        cancellationToken);
    return Ok(result);
  }
}
