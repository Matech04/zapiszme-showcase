using App.Application.Booking.PromoCodes.Queries.ValidatePromoCode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

/// <summary>
/// Anonimowa walidacja kodu promocyjnego dla flow rejestracji. Endpoint nie wymaga slug tenant'a
/// (kody są globalne) i nie inkrementuje <c>CurrentUses</c>. Rate-limited policy
/// <c>PublicBookingWrite</c> chroni przed scanning.
/// </summary>
// UWAGA: route NIE może wpadać pod `api/booking/{slug}/...` (TenantIdentifierMiddleware traktuje
// 3-ci segment jako slug i 404-uje gdy nie znajdzie tenanta). Stąd plain `api/promo`.
[ApiController]
[AllowAnonymous]
[Route("api/promo")]
public class PublicPromoController : ControllerBase
{
  private readonly MediatR.ISender _mediator;

  public PublicPromoController(MediatR.ISender mediator)
  {
    _mediator = mediator;
  }

  [EnableRateLimiting("PublicPromoValidate")]
  [HttpPost("validate")]
  public async Task<ActionResult<ValidatePromoCodeResult>> Validate(
    [FromBody] ValidatePromoCodeRequest request,
    CancellationToken ct)
  {
    var result = await _mediator.Send(new ValidatePromoCodeQuery(request.Code ?? string.Empty), ct);
    return Ok(result);
  }
}

public sealed record ValidatePromoCodeRequest(string Code);
