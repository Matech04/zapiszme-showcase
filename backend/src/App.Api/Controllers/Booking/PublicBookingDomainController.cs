using App.Application.Booking.BookingSalon.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers.Booking;

/// <summary>
/// White-label: rozwiązanie hosta klienta (np. <c>rezerwacja.salon-przyklad.pl</c>) na info salonu
/// + slug. SPA bookingu na customowej domenie woła to przy starcie, bo nie ma slugu w ścieżce.
/// Trasa CELOWO poza wzorcem <c>api/booking/{slug}/...</c> — inaczej <c>TenantIdentifierMiddleware</c>
/// potraktowałby segment jako slug i zwrócił 404 przed kontrolerem.
/// </summary>
[AllowAnonymous]
[Route("api/booking-domains")]
[EnableRateLimiting("PublicBookingRead")]
public class PublicBookingDomainController : BookingApiControllerBase
{
  [HttpGet("resolve")]
  public async Task<ActionResult<PublicBookingSalonInfoDto>> Resolve(
    [FromQuery] string host,
    CancellationToken cancellationToken)
  {
    var result = await Mediator.Send(new ResolveBookingHostQuery(host), cancellationToken);
    return Ok(result);
  }
}
