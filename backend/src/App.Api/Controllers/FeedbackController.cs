using System.Security.Claims;
using App.Application.Feedback.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

public record SubmitFeedbackRequest(string Kind, string Title, string Description, string? PageUrl);

[Authorize(Policy = "GeneralAccess")]
public class FeedbackController : ApiControllerBase
{
  [HttpPost]
  [EnableRateLimiting("FeedbackWrite")]
  public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest request)
  {
    var userEmail = User.FindFirstValue(ClaimTypes.Email)
      ?? User.FindFirstValue("email");

    await Mediator.Send(new SubmitFeedbackCommand(
      request.Kind,
      request.Title,
      request.Description,
      request.PageUrl,
      userEmail));

    return NoContent();
  }
}
