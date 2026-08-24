using App.Api;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers.Booking;

/// <summary>Bazowy kontroler bookingowy — bez wspólnego <c>api/[controller]</c>, żeby trasy miały tylko prefiks <c>api/booking/…</c>.</summary>
[ApiExplorerSettings(GroupName = OpenApiDocuments.BookingApiGroupName)]
[ApiController]
public abstract class BookingApiControllerBase : ControllerBase
{
  private ISender? _mediator;

  protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}
